import requests
import feedparser
import pyodbc
from bs4 import BeautifulSoup
import datetime

# Mock connection string
# In a real app, this should be read from environment variables or a secure vault
CONN_STR = (
    r"Driver={ODBC Driver 17 for SQL Server};"
    r"Server=localhost\SQLEXPRESS;"
    r"Database=ProductHubDB;"
    r"Trusted_Connection=yes;"
)

def get_db_connection():
    return pyodbc.connect(CONN_STR)

def parse_product_hunt():
    print("Scraping Product Hunt RSS...")
    # Product Hunt doesn't have a direct public RSS, using a placeholder/mock
    feed_url = 'https://www.producthunt.com/feed'
    feed = feedparser.parse(feed_url)
    
    products = []
    # Mocking data if feed is blocked
    if len(feed.entries) == 0:
        products.append({
            "name": "AI Startup 1",
            "brand": "Startup Inc",
            "qty": "100",
            "price": 99.99,
            "desc": "A new AI startup building cool things.",
            "rating": 4.5,
            "category": "Artificial Intelligence",
            "launch_date": datetime.datetime.now(),
            "website": "https://aistartup1.example.com",
        })
    else:
        for entry in feed.entries[:5]:
            products.append({
                "name": entry.title,
                "brand": "Unknown",
                "qty": "100",
                "price": 0.0,
                "desc": entry.summary,
                "rating": 0.0,
                "category": "Technology",
                "launch_date": datetime.datetime.now(),
                "website": entry.link,
            })
    return products

def save_to_db(products):
    try:
        conn = get_db_connection()
        cursor = conn.cursor()
        
        for p in products:
            print(f"Saving {p['name']}...")
            cursor.execute("""
                INSERT INTO T_PRODUCTS 
                (F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING, F_CATEGORY, F_LAUNCH_DATE, F_WEBSITE)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """, (p['name'], p['brand'], p['qty'], p['price'], p['desc'], p['rating'], p['category'], p['launch_date'], p['website']))
            
        conn.commit()
        cursor.close()
        conn.close()
        print("Data saved successfully.")
    except Exception as e:
        print(f"Database error: {e}")

if __name__ == "__main__":
    new_products = parse_product_hunt()
    save_to_db(new_products)
