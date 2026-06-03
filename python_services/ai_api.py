from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import ollama
import chromadb
import os

app = FastAPI(title="ProductHub AI Service")

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
    collection = chroma_client.get_collection(name="product_collection")
except Exception as e:
    collection = None
    print(f"Warning: ChromaDB collection not found. RAG disabled. {e}")

class ChatRequest(BaseModel):
    product_name: str
    product_description: str
    user_message: str

@app.post("/chat")
async def chat_with_ollama(request: ChatRequest):
    try:
        rag_context = ""
        # If ChromaDB is available, fetch similar products for extra context
        if collection is not None:
            results = collection.query(
                query_texts=[request.user_message],
                n_results=2
            )
            if results['documents'] and len(results['documents'][0]) > 0:
                rag_context = "\n\nRelated Market Knowledge (from RAG Database):\n" + "\n".join(results['documents'][0])

        system_prompt = f"You are a helpful AI assistant for ProductHub. You are answering questions about a product named '{request.product_name}'. Product info: {request.product_description}. Be concise and helpful.{rag_context}"
        
        messages = [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": request.user_message}
        ]
        
        response = ollama.chat(model='tinyllama', messages=messages)
        return {"reply": response['message']['content']}
    
    except Exception as e:
        # Fallback simulated AI response when Ollama is not installed
        reply = f"🤖 **Simulated AI Reply:**\n\nI see you are asking about **{request.product_name}**. "
        reply += "Since the local Ollama AI engine is not installed on your machine, I am providing a simulated response for demonstration purposes! "
        reply += f"Based on its description: '{request.product_description[:100]}...', this looks like a great product!"
        
        if "price" in request.user_message.lower():
            reply += "\n\n💰 Regarding pricing: The price varies based on market demand, but it offers excellent value!"
        elif "feature" in request.user_message.lower() or "what" in request.user_message.lower():
            reply += "\n\n✨ Key features include its premium build quality, modern design, and robust capabilities."
            
        return {"reply": reply}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
