import requests
import json
import re
import urllib.parse
from bs4 import BeautifulSoup
import ollama

CATEGORIES = {
    "Mobiles": ["Smartphones", "Tablets", "Accessories"],
    "Electronics": ["Laptops", "TVs", "Monitors", "Cameras", "Speakers"],
    "Home Appliances": ["Refrigerator", "Washing Machine", "AC", "Water Purifier", "Microwave"],
    "Grocery": ["Rice", "Oil", "Vegetables", "Packaged Foods", "Beverages"],
    "Plumbing Materials": ["Pipes", "Valves", "Water Tanks", "Fittings"],
    "Electrical Materials": ["Wires", "Switches", "MCB", "Lights", "Fans"],
    "Hardware & Tools": ["Tools", "Fasteners", "Paints"],
    "Automotive": ["Bike Parts", "Car Parts", "Lubricants"],
    "Furniture": ["Sofa", "Bed", "Table", "Chair", "Wardrobe"],
    "Fashion": ["Clothing", "Footwear", "Accessories"],
    "Healthcare": ["Medical Equipment", "Healthcare Products"],
    "Building Materials": ["Cement", "Bricks", "Steel", "Tiles"],
    "Kitchen Products": ["Cookware", "Cutlery", "Chimney", "Blender"]
}

def query_ollama(prompt, system_prompt=None):
    # Preferred order: llama3, mistral, phi3, tinyllama, tinyllama:latest (fallback)
    models = ["llama3", "mistral", "phi3", "tinyllama", "tinyllama:latest"]
    for model in models:
        try:
            messages = []
            if system_prompt:
                messages.append({"role": "system", "content": system_prompt})
            messages.append({"role": "user", "content": prompt})
            
            url = "http://localhost:11434/api/chat"
            payload = {
                "model": model,
                "messages": messages,
                "stream": False,
                "options": {"num_predict": 150}
            }
            response = requests.post(url, json=payload, timeout=6)
            if response.status_code == 200:
                return response.json().get('message', {}).get('content', '')
        except Exception as e:
            print(f"Ollama model {model} failed: {e}")
    return None

def classify_product(name, description=""):
    text = f"{name} {description}".lower()
    
    # Pre-check keyword classification rules (highly responsive fallback)
    for category, subcategories in CATEGORIES.items():
        for sub in subcategories:
            # Check for exact keywords
            keywords = [sub.lower(), category.lower()]
            if sub == "Smartphones":
                keywords.extend(["phone", "mobile", "iphone", "galaxy", "pixel", "oneplus"])
            elif sub == "Laptops":
                keywords.extend(["macbook", "notebook", "laptop", "chromebook"])
            elif sub == "Refrigerator":
                keywords.extend(["fridge", "refrigerator", "freezer"])
            elif sub == "Washing Machine":
                keywords.extend(["washer", "dryer", "washing"])
            elif sub == "Pipes":
                keywords.extend(["pipe", "pvc", "cpvc", "hose"])
            elif sub == "Tools":
                keywords.extend(["hammer", "saw", "drill", "wrench", "screwdriver", "pliers"])
            elif sub == "Clothing":
                keywords.extend(["shirt", "t-shirt", "jeans", "pants", "suit", "jacket", "hoodie"])
            elif sub == "Footwear":
                keywords.extend(["shoe", "boot", "sneaker", "sandal", "heels"])
            
            for kw in keywords:
                if kw in text:
                    return category, sub
                    
    # LLM classification
    categories_schema = {cat: subs for cat, subs in CATEGORIES.items()}
    prompt = (
        f"Classify the product named '{name}' with description '{description}' into one of our predefined Category and Subcategory structures.\n"
        f"Predefined structure: {json.dumps(categories_schema)}\n\n"
        "Return ONLY a valid JSON object matching this structure: {\"category\": \"...\", \"subcategory\": \"...\"}. "
        "Do not include any explanation or extra text. If uncertain, default to Electronics and Laptops."
    )
    
    response = query_ollama(prompt, "You are a product catalog classifier. Answer with a JSON object only.")
    if response:
        try:
            # Parse JSON from response
            match = re.search(r'\{.*?\}', response, re.DOTALL)
            if match:
                data = json.loads(match.group(0))
                cat = data.get("category")
                sub = data.get("subcategory")
                if cat in CATEGORIES and sub in CATEGORIES[cat]:
                    return cat, sub
        except Exception as e:
            print(f"Error parsing LLM classification: {e}")
            
    # Default fallback
    return "Electronics", "Laptops"

def fetch_wikidata_info(query):
    try:
        url = f"https://www.wikidata.org/w/api.php?action=wbsearchentities&search={urllib.parse.quote(query)}&language=en&format=json"
        headers = {"User-Agent": "ProductHub/1.0 (contact@example.local)"}
        res = requests.get(url, headers=headers, timeout=5)
        if res.status_code == 200:
            data = res.json()
            if data.get("search"):
                entity = data["search"][0]
                q_id = entity["id"]
                desc = entity.get("description", "")
                
                # Fetch detailed entity claims
                entity_url = f"https://www.wikidata.org/wiki/Special:EntityData/{q_id}.json"
                detail_res = requests.get(entity_url, headers=headers, timeout=5)
                website = ""
                if detail_res.status_code == 200:
                    detail_data = detail_res.json()
                    claims = detail_data.get("entities", {}).get(q_id, {}).get("claims", {})
                    # P856 is official website
                    if "P856" in claims:
                        website = claims["P856"][0].get("mainsnak", {}).get("datavalue", {}).get("value", "")
                
                return {
                    "wikidata_id": q_id,
                    "description": desc,
                    "website": website
                }
    except Exception as e:
        print(f"Wikidata lookup failed: {e}")
    return None

def scrape_wikipedia(query):
    try:
        url = f"https://en.wikipedia.org/api/rest_v1/page/summary/{urllib.parse.quote(query.replace(' ', '_'))}"
        headers = {"User-Agent": "ProductHub/1.0 (contact@example.local)"}
        res = requests.get(url, headers=headers, timeout=5)
        if res.status_code == 200:
            data = res.json()
            return {
                "summary": data.get("extract", ""),
                "wikipedia_url": data.get("content_urls", {}).get("desktop", {}).get("page", ""),
                "thumbnail": data.get("thumbnail", {}).get("source", "")
            }
    except Exception as e:
        print(f"Wikipedia lookup failed: {e}")
    return None

def search_ddg_images(query):
    # Returns a list of scraped image URLs from DuckDuckGo search or other trusted sources
    images = []
    try:
        # DDG search query
        search_query = f"{query} product image"
        url = f"https://html.duckduckgo.com/html/?q={urllib.parse.quote(search_query)}"
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        }
        res = requests.get(url, headers=headers, timeout=8)
        if res.status_code == 200:
            soup = BeautifulSoup(res.text, "html.parser")
            # Look for image links or regular external links that look like product sites
            for a in soup.find_all("a", href=True):
                href = a["href"]
                # Clean DDG redirect links
                if "/l/?kh=-1&uddg=" in href:
                    href = urllib.parse.unquote(href.split("uddg=")[1].split("&")[0])
                
                # Check if link represents an image file
                if any(ext in href.lower() for ext in [".jpg", ".jpeg", ".png", ".webp"]):
                    if href not in images and not href.startswith("https://duckduckgo.com"):
                        images.append(href)
                        if len(images) >= 5:
                            break
    except Exception as e:
        print(f"DDG Image scraping failed: {e}")
        
    return images

def enrich_product_details(name, brand=""):
    # Combine Wikipedia, Wikidata and DDG to enrich product details
    query = name
    if brand and brand != "Unknown" and brand not in name:
        query = f"{brand} {name}"
        
    wiki_data = scrape_wikipedia(query)
    if not wiki_data and brand:
        wiki_data = scrape_wikipedia(brand)
        
    wikidata_data = fetch_wikidata_info(query)
    if not wikidata_data and brand:
        wikidata_data = fetch_wikidata_info(brand)
        
    scraped_images = search_ddg_images(query)
    
    # Collect specifications, features, and reviews via LLM synthesis of web search
    specs = {}
    features = []
    reviews_summary = "Excellent product rated highly by technology enthusiasts and consumers for its durability and innovative features."
    
    # We can ask Ollama to generate standard specs, features, and reviews summary based on its training knowledge
    enrich_prompt = (
        f"Generate technical specifications, key features, and reviews summary for the product '{query}'.\n"
        "Return ONLY a valid JSON object matching this structure:\n"
        "{\n"
        "  \"specifications\": {\"key\": \"value\", \"key2\": \"value2\"},\n"
        "  \"features\": [\"feature1\", \"feature2\"],\n"
        "  \"reviews_summary\": \"summarized reviews here\"\n"
        "}\n"
        "Keep specifications standard (e.g., Weight, Dimensions, Battery, Processor, Material depending on category)."
    )
    
    llm_resp = query_ollama(enrich_prompt, "You are a product data enricher. Output JSON only.")
    if llm_resp:
        try:
            match = re.search(r'\{.*?\}', llm_resp, re.DOTALL)
            if match:
                enrich_data = json.loads(match.group(0))
                specs = enrich_data.get("specifications", {})
                features = enrich_data.get("features", [])
                reviews_summary = enrich_data.get("reviews_summary", reviews_summary)
        except Exception as e:
            print(f"Error parsing LLM enrichment specs: {e}")
            
    # Gather related products
    related_prompt = f"List 4 related or alternative products for '{query}'. Return ONLY a JSON list of strings."
    related_resp = query_ollama(related_prompt, "Return a JSON list of strings only.")
    related_products = []
    if related_resp:
        try:
            match = re.search(r'\[.*?\]', related_resp, re.DOTALL)
            if match:
                related_products = json.loads(match.group(0))
        except Exception as e:
            print(f"Error parsing LLM related products: {e}")
            
    # Assemble final enrichment payload
    description = wiki_data.get("summary", "") if wiki_data else (wikidata_data.get("description", "") if wikidata_data else "")
    wikipedia_url = wiki_data.get("wikipedia_url", "") if wiki_data else ""
    website = wikidata_data.get("website", "") if wikidata_data else ""
    
    # Combine Wikipedia thumbnail with scraped images
    all_images = []
    if wiki_data and wiki_data.get("thumbnail"):
        all_images.append(wiki_data["thumbnail"])
    for img in scraped_images:
        if img not in all_images:
            all_images.append(img)
            
    # Fallback to demo images if empty
    if not all_images:
        all_images = [
            "https://images.unsplash.com/photo-1505740420928-5e560c06d30e?w=500",
            "https://images.unsplash.com/photo-1523275335684-37898b6baf30?w=500"
        ]
        
    return {
        "description": description if description else "Innovative technology product designed for modern workspaces.",
        "wikipedia_url": wikipedia_url,
        "website": website,
        "images": all_images,
        "specifications": specs if specs else {"Display": "Vibrant screen", "Connectivity": "Wi-Fi, Bluetooth"},
        "features": features if features else ["Premium design", "High durability", "Energy efficient"],
        "reviews_summary": reviews_summary,
        "related_products": related_products if related_products else ["Alternative Model A", "Alternative Model B"]
    }
