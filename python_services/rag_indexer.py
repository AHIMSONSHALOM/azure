import pyodbc
import chromadb
from chromadb.utils import embedding_functions
import os

# Connection String for SQL Server
CONN_STR = (
    r"Driver={ODBC Driver 17 for SQL Server};"
    r"Server=(localdb)\MSSQLLocalDB;"
    r"Database=ProductHubDB;"
    r"Trusted_Connection=yes;"
)

# Initialize ChromaDB client (local persistent storage)
CHROMA_DB_DIR = os.path.join(os.path.dirname(__file__), "chroma_db")
chroma_client = chromadb.PersistentClient(path=CHROMA_DB_DIR)

# Use a lightweight embedding function (SentenceTransformers is default for Chroma)
sentence_transformer_ef = embedding_functions.DefaultEmbeddingFunction()

def get_db_connection():
    return pyodbc.connect(CONN_STR)

def index_products():
    print("Starting Product Indexing for RAG...")
    
    # Get or Create the collection
    collection = chroma_client.get_or_create_collection(
        name="product_collection",
        embedding_function=sentence_transformer_ef
    )
    
    conn = get_db_connection()
    cursor = conn.cursor()
    
    # Fetch all products
    cursor.execute("""
        SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_PROD_DESC, F_CATEGORY
        FROM T_PRODUCTS
        WHERE F_PROD_DESC IS NOT NULL AND LTRIM(RTRIM(F_PROD_DESC)) != ''
    """)
    products = cursor.fetchall()
    
    documents = []
    metadatas = []
    ids = []
    
    print(f"Found {len(products)} products to index.")
    
    for prod in products:
        prod_id = str(prod[0])
        name = prod[1]
        brand = prod[2]
        desc = prod[3]
        category = prod[4]
        
        # Combine fields to create a rich document for embedding
        document_text = f"{name} by {brand}. Category: {category}. Description: {desc}"
        
        documents.append(document_text)
        metadatas.append({
            "product_id": prod_id,
            "name": name,
            "brand": brand,
            "category": category if category else "Uncategorized"
        })
        ids.append(prod_id)
        
    if ids:
        # Upsert into Chroma (Insert or Update)
        print("Upserting vectors into ChromaDB...")
        collection.upsert(
            documents=documents,
            metadatas=metadatas,
            ids=ids
        )
        print("Indexing completed successfully!")
    else:
        print("No products with descriptions found to index.")
        
    cursor.close()
    conn.close()

if __name__ == "__main__":
    index_products()
