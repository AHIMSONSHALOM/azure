import pyodbc
import requests
import json
import time

# Mock connection string
CONN_STR = (
    r"Driver={ODBC Driver 17 for SQL Server};"
    r"Server=(localdb)\MSSQLLocalDB;"
    r"Database=ProductHubDB;"
    r"Trusted_Connection=yes;"
)

def get_db_connection():
    return pyodbc.connect(CONN_STR)

def fetch_wikipedia_summary(query):
    print(f"Fetching Wikipedia summary for '{query}'...")
    url = f"https://en.wikipedia.org/api/rest_v1/page/summary/{query.replace(' ', '_')}"
    headers = {
        'User-Agent': 'ProductHub-AI-Enricher/1.0 (contact@example.com)'
    }
    
    try:
        response = requests.get(url, headers=headers)
        if response.status_code == 200:
            data = response.json()
            return {
                "summary": data.get("extract", ""),
                "url": data.get("content_urls", {}).get("desktop", {}).get("page", "")
            }
        return None
    except Exception as e:
        print(f"Error fetching from Wikipedia: {e}")
        return None

def enrich_products():
    try:
        conn = get_db_connection()
        cursor = conn.cursor()
        
        # Find products missing Wikipedia URLs or Descriptions
        cursor.execute("""
            SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND 
            FROM T_PRODUCTS 
            WHERE F_WIKIPEDIA_URL IS NULL OR F_WIKIPEDIA_URL = ''
        """)
        products = cursor.fetchall()
        
        if not products:
            print("No products need enrichment.")
            return

        for prod in products:
            prod_id, prod_name, brand = prod
            
            # Try searching Wikipedia by Product Name first, then by Brand
            search_query = prod_name
            wiki_data = fetch_wikipedia_summary(search_query)
            
            if not wiki_data and brand and brand != "Unknown":
                print(f"Falling back to brand search: '{brand}'")
                wiki_data = fetch_wikipedia_summary(brand)
                
            if wiki_data:
                print(f"Updating Product ID {prod_id} with Wikipedia data...")
                # Update the database
                cursor.execute("""
                    UPDATE T_PRODUCTS 
                    SET F_WIKIPEDIA_URL = ?, F_PROD_DESC = CASE WHEN F_PROD_DESC IS NULL OR F_PROD_DESC = '' THEN ? ELSE F_PROD_DESC END
                    WHERE F_PRODUCT_ID = ?
                """, (wiki_data['url'], wiki_data['summary'], prod_id))
                conn.commit()
            
            # Be nice to the Wikipedia API
            time.sleep(1)
            
        cursor.close()
        conn.close()
        print("Enrichment complete.")
    except Exception as e:
        print(f"Database connection error: {e}")

if __name__ == "__main__":
    print("Starting Wikipedia Enricher Service...")
    enrich_products()
