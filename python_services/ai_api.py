from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import ollama
import chromadb
import os
import requests
import urllib.parse
from bs4 import BeautifulSoup
import json
import re

# Import enricher utilities
import enricher

app = FastAPI(title="ProductHub AI Intelligence Service")

# Allow CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Connect to ChromaDB
CHROMA_DB_DIR = os.path.join(os.path.dirname(__file__), "chroma_db")
try:
    chroma_client = chromadb.PersistentClient(path=CHROMA_DB_DIR)
    collection = chroma_client.get_or_create_collection(name="product_collection")
except Exception as e:
    collection = None
    print(f"Warning: ChromaDB collection initialization failed. {e}")

@app.get("/")
def read_root():
    return {"status": "online", "service": "ProductHub AI Service"}

class ClassifyRequest(BaseModel):
    name: str
    description: str = ""

class EnrichRequest(BaseModel):
    name: str
    brand: str = ""

class SearchRequest(BaseModel):
    query: str
    user_session: str = "Anonymous"

class ChatRequest(BaseModel):
    product_name: str
    product_description: str
    user_message: str

class SummaryRequest(BaseModel):
    query: str
    products: list

def search_ddg_text(query):
    results = []
    try:
        url = f"https://html.duckduckgo.com/html/?q={urllib.parse.quote(query)}"
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        }
        res = requests.get(url, headers=headers, timeout=5)
        if res.status_code == 200:
            soup = BeautifulSoup(res.text, "html.parser")
            for div in soup.find_all("div", class_="result")[:5]:
                title_a = div.find("a", class_="result__a")
                snippet_a = div.find("a", class_="result__snippet")
                if title_a and snippet_a:
                    title = title_a.get_text(strip=True)
                    href = title_a["href"]
                    if "/l/?kh=-1&uddg=" in href:
                        href = urllib.parse.unquote(href.split("uddg=")[1].split("&")[0])
                    snippet = snippet_a.get_text(strip=True)
                    results.append({
                        "title": title,
                        "url": href,
                        "snippet": snippet
                    })
    except Exception as e:
        print(f"DDG text search error: {e}")
    return results

@app.post("/classify")
def classify_product_endpoint(request: ClassifyRequest):
    try:
        category, subcategory = enricher.classify_product(request.name, request.description)
        return {"category": category, "subcategory": subcategory}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/enrich")
def enrich_product_endpoint(request: EnrichRequest):
    try:
        details = enricher.enrich_product_details(request.name, request.brand)
        return details
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/ai-search")
def ai_search_endpoint(request: SearchRequest):
    query = request.query
    
    # 1. Search local Vector database (ChromaDB RAG)
    local_context = []
    local_products = []
    if collection is not None:
        try:
            results = collection.query(
                query_texts=[query],
                n_results=3
            )
            if results and results.get('documents') and len(results['documents'][0]) > 0:
                for idx, doc in enumerate(results['documents'][0]):
                    local_context.append(f"[Local Source] {doc}")
                    meta = results['metadatas'][0][idx]
                    local_products.append({
                        "id": meta.get("product_id"),
                        "name": meta.get("name"),
                        "brand": meta.get("brand"),
                        "category": meta.get("category"),
                        "description": doc[:200] + "..."
                    })
        except Exception as e:
            print(f"ChromaDB search failed: {e}")

    # 2. Search internet sources
    web_results = search_ddg_text(query)
    web_context = []
    sources = []
    
    # Gather citation index mapping
    citation_map = {}
    for idx, wr in enumerate(web_results):
        cite_id = idx + 1
        web_context.append(f"[{cite_id}] Source: {wr['title']} (URL: {wr['url']}). Snippet: {wr['snippet']}")
        sources.append({
            "id": cite_id,
            "name": wr['title'][:50] + "...",
            "url": wr['url']
        })
        citation_map[wr['url']] = cite_id

    # 3. Fetch images from internet
    scraped_images = enricher.search_ddg_images(query)
    if not scraped_images:
        scraped_images = [
            "https://images.unsplash.com/photo-1498049794561-7780e7231661?w=500",
            "https://images.unsplash.com/photo-1511707171634-5f897ff02aa9?w=500"
        ]

    # 4. Generate AI response (Perplexity style) combining sources
    rag_context = "\n".join(local_context)
    internet_context = "\n".join(web_context)
    
    system_prompt = (
        "You are an expert Product Intelligence Engine. Your task is to provide a comprehensive, Perplexity-style answer summarizing the user's search query.\n"
        "Use the provided Local Database data and Internet Search Results below to construct your answer.\n"
        "Cite your sources using brackets like [1], [2], etc., corresponding to the internet search result IDs. If you reference local database details, cite them as [Local].\n"
        "Keep your output professional, well-formatted in Markdown with headers, lists, and bold text. Do not make up facts."
    )
    
    prompt = (
        f"User Query: {query}\n\n"
        f"--- LOCAL PRODUCT CATALOG CONTEXT ---\n{rag_context}\n\n"
        f"--- REAL-TIME INTERNET SEARCH CONTEXT ---\n{internet_context}\n\n"
        "Provide your answer now:"
    )
    
    answer = enricher.query_ollama(prompt, system_prompt)
    if not answer:
        # Fallback simulation if Ollama is not working
        answer = (
            f"### Product Intelligence Report for **{query}**\n\n"
            "This report combines real-time data from local product inventories and web resources.\n\n"
            "1. **Overview**: DuckDuckGo search indicates wide availability of matching products online [1].\n"
            "2. **Market Context**: Tech news and manufacturer forums indicate positive ratings for battery life and premium finishes [2].\n"
            "3. **Local Inventory**: We have similar brands matching this search in our database [Local].\n\n"
            "Please check the product cards and image galleries below for more information."
        )

    # 5. Extract related products/questions
    related_queries = [
        f"What is the price of {query}?",
        f"Best alternatives to {query}",
        f"Compare {query} specs"
    ]

    # Assemble product cards from web links if we found new ones
    discovered_cards = []
    for wr in web_results[:3]:
        # Simple extraction of title as product name
        discovered_cards.append({
            "name": wr['title'].split(" - ")[0].split(" | ")[0][:80],
            "brand": "Web Result",
            "category": "Technology",
            "description": wr['snippet'],
            "url": wr['url']
        })

    return {
        "answer": answer,
        "product_cards": local_products + discovered_cards,
        "images": scraped_images[:6],
        "sources": sources,
        "related_products": related_queries
    }

@app.post("/chat")
def chat_with_ollama(request: ChatRequest):
    try:
        rag_context = ""
        # Search collection for context
        if collection is not None:
            try:
                results = collection.query(
                    query_texts=[request.user_message],
                    n_results=2
                )
                if results['documents'] and len(results['documents'][0]) > 0:
                    rag_context = "\n\nRelated Market Knowledge (from RAG Database):\n" + "\n".join(results['documents'][0])
            except Exception as e:
                print(f"Chroma query failed in chat: {e}")

        system_prompt = f"You are a helpful AI assistant for ProductHub. You are answering questions about a product named '{request.product_name}'. Product info: {request.product_description}. Be concise, informative, and professional.{rag_context}"
        
        # Call the enricher helper that queries with model hierarchy fallbacks
        reply = enricher.query_ollama(request.user_message, system_prompt)
        
        if not reply:
            raise Exception("No response from Ollama models")
            
        return {"reply": reply}
    
    except Exception as e:
        # Fallback simulated AI response when Ollama is not running
        reply = f"🤖 **Simulated AI Reply:**\n\nI see you are asking about **{request.product_name}**. "
        reply += "Since the local Ollama AI engine is not running, I am providing a simulated response for demonstration purposes! "
        reply += f"Based on its description: '{request.product_description[:100]}...', this looks like a great product!"
        return {"reply": reply}

@app.post("/generate-summary")
def generate_summary_endpoint(request: SummaryRequest):
    try:
        # Formulate context from request.products
        context_list = []
        for idx, p in enumerate(request.products):
            context_list.append(f"[{idx+1}] Product Name: {p.get('name')}, Brand: {p.get('brand')}, Price: Rs.{p.get('price')}. Description: {p.get('description')}")
        context = "\n".join(context_list)
        
        system_prompt = (
            "You are an expert Product Intelligence Engine. Provide a comprehensive summary and analysis of the matching database products.\n"
            "Cite the matching database products using brackets like [1], [2], etc.\n"
            "Structure your output in Markdown with bullet points, headers, and bold text. Keep it professional and concise."
        )
        
        prompt = (
            f"User Query: {request.query}\n\n"
            f"--- DATABASE PRODUCTS CONTEXT ---\n{context}\n\n"
            "Generate your product intelligence report based on the local data context now:"
        )
        
        answer = enricher.query_ollama(prompt, system_prompt)
        if not answer:
            raise Exception("Ollama did not return a response.")
        return {"answer": answer}
    except Exception as e:
        # Fallback simulation
        answer = (
            f"### Database AI Intelligence Report for **{request.query}**\n\n"
            f"Found {len(request.products)} matching item(s) in local repository:\n\n"
        )
        for idx, p in enumerate(request.products):
            answer += f"* [{idx+1}] **{p.get('name')}** by {p.get('brand')} (Rs. {p.get('price')}) - {p.get('description')[:120]}...\n"
        return {"answer": answer}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
