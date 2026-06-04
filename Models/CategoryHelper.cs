using System;
using System.Collections.Generic;
using System.Linq;

namespace ProductHub_MVC.Models
{
    public static class CategoryHelper
    {
        public static readonly Dictionary<string, List<string>> Categories = new()
        {
            { "Mobiles", new() { "Smartphones", "Tablets", "Accessories" } },
            { "Electronics", new() { "Laptops", "TVs", "Monitors", "Cameras", "Speakers" } },
            { "Home Appliances", new() { "Refrigerator", "Washing Machine", "AC", "Water Purifier", "Microwave" } },
            { "Grocery", new() { "Rice", "Oil", "Vegetables", "Packaged Foods", "Beverages" } },
            { "Plumbing Materials", new() { "Pipes", "Valves", "Water Tanks", "Fittings" } },
            { "Electrical Materials", new() { "Wires", "Switches", "MCB", "Lights", "Fans" } },
            { "Hardware & Tools", new() { "Tools", "Fasteners", "Paints" } },
            { "Automotive", new() { "Bike Parts", "Car Parts", "Lubricants" } },
            { "Furniture", new() { "Sofa", "Bed", "Table", "Chair", "Wardrobe" } },
            { "Fashion", new() { "Clothing", "Footwear", "Accessories" } },
            { "Healthcare", new() { "Medical Equipment", "Healthcare Products" } },
            { "Building Materials", new() { "Cement", "Bricks", "Steel", "Tiles" } },
            { "Kitchen Products", new() { "Cookware", "Cutlery", "Chimney", "Blender" } }
        };

        public static bool IsValid(string category, string subcategory)
        {
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(subcategory))
                return false;

            return Categories.TryGetValue(category, out var subcategories) && 
                   subcategories.Contains(subcategory);
        }

        public static (string Category, string Subcategory) ClassifyFallback(string name, string description)
        {
            string text = $"{name} {description}".ToLower();

            // Simple rules for fallback classification
            if (text.Contains("phone") || text.Contains("iphone") || text.Contains("samsung galaxy") || text.Contains("pixel") || text.Contains("mobile"))
                return ("Mobiles", "Smartphones");
            if (text.Contains("tablet") || text.Contains("ipad"))
                return ("Mobiles", "Tablets");
            if (text.Contains("charger") || text.Contains("headphone") || text.Contains("earbud") || text.Contains("case"))
                return ("Mobiles", "Accessories");

            if (text.Contains("laptop") || text.Contains("macbook") || text.Contains("notebook") || text.Contains("pc"))
                return ("Electronics", "Laptops");
            if (text.Contains("tv") || text.Contains("television"))
                return ("Electronics", "TVs");
            if (text.Contains("monitor") || text.Contains("display"))
                return ("Electronics", "Monitors");
            if (text.Contains("camera") || text.Contains("lens") || text.Contains("dslr"))
                return ("Electronics", "Cameras");
            if (text.Contains("speaker") || text.Contains("soundbar") || text.Contains("audio"))
                return ("Electronics", "Speakers");

            if (text.Contains("fridge") || text.Contains("refrigerator"))
                return ("Home Appliances", "Refrigerator");
            if (text.Contains("wash") || text.Contains("dryer") || text.Contains("washing machine"))
                return ("Home Appliances", "Washing Machine");
            if (text.Contains("ac ") || text.Contains("air conditioner") || text.Contains("cooling"))
                return ("Home Appliances", "AC");
            if (text.Contains("water purifier") || text.Contains("filter"))
                return ("Home Appliances", "Water Purifier");
            if (text.Contains("microwave") || text.Contains("oven"))
                return ("Home Appliances", "Microwave");

            if (text.Contains("rice") || text.Contains("grain"))
                return ("Grocery", "Rice");
            if (text.Contains("oil") || text.Contains("mustard") || text.Contains("olive"))
                return ("Grocery", "Oil");
            if (text.Contains("vegetable") || text.Contains("tomato") || text.Contains("potato") || text.Contains("onion"))
                return ("Grocery", "Vegetables");
            if (text.Contains("packaged") || text.Contains("snack") || text.Contains("chip") || text.Contains("biscuit"))
                return ("Grocery", "Packaged Foods");
            if (text.Contains("beverage") || text.Contains("juice") || text.Contains("soda") || text.Contains("cola") || text.Contains("tea") || text.Contains("coffee"))
                return ("Grocery", "Beverages");

            if (text.Contains("pipe") || text.Contains("pvc"))
                return ("Plumbing Materials", "Pipes");
            if (text.Contains("valve"))
                return ("Plumbing Materials", "Valves");
            if (text.Contains("tank") || text.Contains("water tank"))
                return ("Plumbing Materials", "Water Tanks");
            if (text.Contains("fitting") || text.Contains("elbow") || text.Contains("connector"))
                return ("Plumbing Materials", "Fittings");

            if (text.Contains("wire") || text.Contains("cable"))
                return ("Electrical Materials", "Wires");
            if (text.Contains("switch") || text.Contains("socket"))
                return ("Electrical Materials", "Switches");
            if (text.Contains("mcb") || text.Contains("breaker"))
                return ("Electrical Materials", "MCB");
            if (text.Contains("light") || text.Contains("bulb") || text.Contains("led"))
                return ("Electrical Materials", "Lights");
            if (text.Contains("fan") || text.Contains("ceiling fan"))
                return ("Electrical Materials", "Fans");

            if (text.Contains("screw") || text.Contains("bolt") || text.Contains("nail") || text.Contains("fastener"))
                return ("Hardware & Tools", "Fasteners");
            if (text.Contains("paint") || text.Contains("brush") || text.Contains("coat"))
                return ("Hardware & Tools", "Paints");
            if (text.Contains("hammer") || text.Contains("drill") || text.Contains("saw") || text.Contains("screwdriver") || text.Contains("wrench") || text.Contains("tool"))
                return ("Hardware & Tools", "Tools");

            if (text.Contains("bike") || text.Contains("motorcycle") || text.Contains("helmet"))
                return ("Automotive", "Bike Parts");
            if (text.Contains("car ") || text.Contains("brake") || text.Contains("tire") || text.Contains("wiper"))
                return ("Automotive", "Car Parts");
            if (text.Contains("lubricant") || text.Contains("engine oil") || text.Contains("grease"))
                return ("Automotive", "Lubricants");

            if (text.Contains("sofa") || text.Contains("couch"))
                return ("Furniture", "Sofa");
            if (text.Contains("bed") || text.Contains("mattress"))
                return ("Furniture", "Bed");
            if (text.Contains("table") || text.Contains("desk"))
                return ("Furniture", "Table");
            if (text.Contains("chair") || text.Contains("stool"))
                return ("Furniture", "Chair");
            if (text.Contains("wardrobe") || text.Contains("closet") || text.Contains("cabinet"))
                return ("Furniture", "Wardrobe");

            if (text.Contains("shirt") || text.Contains("t-shirt") || text.Contains("pants") || text.Contains("jeans") || text.Contains("clothing") || text.Contains("dress") || text.Contains("jacket"))
                return ("Fashion", "Clothing");
            if (text.Contains("shoe") || text.Contains("sneaker") || text.Contains("boot") || text.Contains("sandal") || text.Contains("footwear"))
                return ("Fashion", "Footwear");
            if (text.Contains("bag") || text.Contains("watch") || text.Contains("belt") || text.Contains("sunglasses"))
                return ("Fashion", "Accessories");

            if (text.Contains("medical") || text.Contains("stethoscope") || text.Contains("thermometer") || text.Contains("bp monitor") || text.Contains("equipment"))
                return ("Healthcare", "Medical Equipment");
            if (text.Contains("healthcare") || text.Contains("medicine") || text.Contains("supplement") || text.Contains("vitamin") || text.Contains("mask"))
                return ("Healthcare", "Healthcare Products");

            if (text.Contains("cement") || text.Contains("concrete"))
                return ("Building Materials", "Cement");
            if (text.Contains("brick") || text.Contains("block"))
                return ("Building Materials", "Bricks");
            if (text.Contains("steel") || text.Contains("rod") || text.Contains("bar"))
                return ("Building Materials", "Steel");
            if (text.Contains("tile") || text.Contains("marble"))
                return ("Building Materials", "Tiles");

            if (text.Contains("cookware") || text.Contains("pan") || text.Contains("pot") || text.Contains("cooker"))
                return ("Kitchen Products", "Cookware");
            if (text.Contains("knife") || text.Contains("fork") || text.Contains("spoon") || text.Contains("cutlery"))
                return ("Kitchen Products", "Cutlery");
            if (text.Contains("chimney") || text.Contains("exhaust"))
                return ("Kitchen Products", "Chimney");
            if (text.Contains("blender") || text.Contains("mixer") || text.Contains("grinder") || text.Contains("juicer"))
                return ("Kitchen Products", "Blender");

            // Default fallback
            return ("Electronics", "Laptops");
        }
    }
}
