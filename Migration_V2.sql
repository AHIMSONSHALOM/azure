-- ==========================================
-- PHASE 1: Add new tables and keep old columns
-- ==========================================

-- 1. Create T_CATEGORIES
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[T_CATEGORIES]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[T_CATEGORIES] (
        [CategoryId] INT IDENTITY(1,1) PRIMARY KEY,
        [CategoryName] NVARCHAR(255) NOT NULL UNIQUE
    );
END
GO

-- 2. Create T_BRANDS
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[T_BRANDS]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[T_BRANDS] (
        [BrandId] INT IDENTITY(1,1) PRIMARY KEY,
        [BrandName] NVARCHAR(255) NOT NULL UNIQUE,
        [WebsiteUrl] NVARCHAR(500) NULL
    );
END
GO

-- 3. Seed standard categories
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[T_CATEGORIES]') AND type in (N'U'))
BEGIN
    INSERT INTO [dbo].[T_CATEGORIES] (CategoryName)
    SELECT CatName FROM (
        VALUES 
        (N'Mobiles'), (N'Electronics'), (N'Home Appliances'), (N'Grocery'), 
        (N'Plumbing Materials'), (N'Electrical Materials'), (N'Hardware & Tools'), 
        (N'Automotive'), (N'Healthcare'), (N'Fashion'), (N'Furniture'), 
        (N'Kitchen Products'), (N'Building Materials')
    ) AS Temp(CatName)
    WHERE CatName NOT IN (SELECT CategoryName FROM [dbo].[T_CATEGORIES]);
END
GO

-- 4. Alter T_PRODUCTS to add F_CATEGORY_ID and F_BRAND_ID
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCTS]') AND name = N'F_CATEGORY_ID')
BEGIN
    ALTER TABLE [dbo].[T_PRODUCTS] ADD [F_CATEGORY_ID] INT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCTS]') AND name = N'F_BRAND_ID')
BEGIN
    ALTER TABLE [dbo].[T_PRODUCTS] ADD [F_BRAND_ID] INT NULL;
END
GO

-- 5. Modify T_PRODUCT_IMAGES: Add SourceUrl column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_IMAGES]') AND name = N'SourceUrl')
BEGIN
    ALTER TABLE [dbo].[T_PRODUCT_IMAGES] ADD [SourceUrl] NVARCHAR(1000) NULL;
END
GO

-- Rename Id to ImageId in T_PRODUCT_IMAGES
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_IMAGES]') AND name = N'Id')
BEGIN
    EXEC sp_rename 'T_PRODUCT_IMAGES.Id', 'ImageId', 'COLUMN';
END
GO

-- Rename Id to SourceId in T_PRODUCT_SOURCES
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_SOURCES]') AND name = N'Id')
BEGIN
    EXEC sp_rename 'T_PRODUCT_SOURCES.Id', 'SourceId', 'COLUMN';
END
GO

-- Rename Id to NewsId in T_PRODUCT_NEWS
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_NEWS]') AND name = N'Id')
BEGIN
    EXEC sp_rename 'T_PRODUCT_NEWS.Id', 'NewsId', 'COLUMN';
END
GO

-- Add PublishedDate to T_PRODUCT_NEWS
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_NEWS]') AND name = N'PublishedDate')
BEGIN
    ALTER TABLE [dbo].[T_PRODUCT_NEWS] ADD [PublishedDate] DATETIME NULL;
END
GO

-- Rename Id to AnalyticsId in T_PRODUCT_ANALYTICS
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_ANALYTICS]') AND name = N'Id')
BEGIN
    EXEC sp_rename 'T_PRODUCT_ANALYTICS.Id', 'AnalyticsId', 'COLUMN';
END
GO

-- Add Views, Searches, LastViewed to T_PRODUCT_ANALYTICS
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_ANALYTICS]') AND name = N'Views')
BEGIN
    ALTER TABLE [dbo].[T_PRODUCT_ANALYTICS] ADD [Views] INT DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_ANALYTICS]') AND name = N'Searches')
BEGIN
    ALTER TABLE [dbo].[T_PRODUCT_ANALYTICS] ADD [Searches] INT DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_ANALYTICS]') AND name = N'LastViewed')
BEGIN
    ALTER TABLE [dbo].[T_PRODUCT_ANALYTICS] ADD [LastViewed] DATETIME NULL;
END
GO

-- 6. Add Indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PRODUCTS_F_PROD_NAME' AND object_id = OBJECT_ID(N'[dbo].[T_PRODUCTS]'))
BEGIN
    CREATE INDEX [IX_T_PRODUCTS_F_PROD_NAME] ON [dbo].[T_PRODUCTS]([F_PROD_NAME]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PRODUCTS_F_BRAND_ID' AND object_id = OBJECT_ID(N'[dbo].[T_PRODUCTS]'))
BEGIN
    CREATE INDEX [IX_T_PRODUCTS_F_BRAND_ID] ON [dbo].[T_PRODUCTS]([F_BRAND_ID]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PRODUCTS_F_CATEGORY_ID' AND object_id = OBJECT_ID(N'[dbo].[T_PRODUCTS]'))
BEGIN
    CREATE INDEX [IX_T_PRODUCTS_F_CATEGORY_ID] ON [dbo].[T_PRODUCTS]([F_CATEGORY_ID]);
END
GO

-- ==========================================
-- PHASE 2: Migrate existing data
-- ==========================================

-- Populate T_BRANDS from unique F_BRAND
INSERT INTO [dbo].[T_BRANDS] (BrandName, WebsiteUrl)
SELECT DISTINCT F_BRAND, NULL FROM [dbo].[T_PRODUCTS]
WHERE F_BRAND IS NOT NULL AND TRIM(F_BRAND) <> ''
AND F_BRAND NOT IN (SELECT BrandName FROM [dbo].[T_BRANDS]);
GO

-- Populate T_CATEGORIES from any missing F_CATEGORY
INSERT INTO [dbo].[T_CATEGORIES] (CategoryName)
SELECT DISTINCT F_CATEGORY FROM [dbo].[T_PRODUCTS]
WHERE F_CATEGORY IS NOT NULL AND TRIM(F_CATEGORY) <> ''
AND F_CATEGORY NOT IN (SELECT CategoryName FROM [dbo].[T_CATEGORIES]);
GO

-- Update product Brand/Category foreign keys
UPDATE p
SET p.F_BRAND_ID = b.BrandId
FROM [dbo].[T_PRODUCTS] p
JOIN [dbo].[T_BRANDS] b ON p.F_BRAND = b.BrandName;
GO

UPDATE p
SET p.F_CATEGORY_ID = c.CategoryId
FROM [dbo].[T_PRODUCTS] p
JOIN [dbo].[T_CATEGORIES] c ON p.F_CATEGORY = c.CategoryName;
GO

-- Populate SourceUrl from ImageUrl in T_PRODUCT_IMAGES if null
UPDATE [dbo].[T_PRODUCT_IMAGES]
SET SourceUrl = ImageUrl
WHERE SourceUrl IS NULL;
GO

-- Populate PublishedDate from PublishDate in T_PRODUCT_NEWS
UPDATE [dbo].[T_PRODUCT_NEWS]
SET PublishedDate = PublishDate
WHERE PublishedDate IS NULL AND PublishDate IS NOT NULL;
GO

-- Populate T_PRODUCT_ANALYTICS views and last viewed from T_ANALYTICS
INSERT INTO [dbo].[T_PRODUCT_ANALYTICS] (ProductId, Views, Searches, LastViewed)
SELECT F_PRODUCT_ID, F_VIEW_COUNT, 0, F_LAST_VIEWED FROM [dbo].[T_ANALYTICS] a
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[T_PRODUCT_ANALYTICS] pa WHERE pa.ProductId = a.F_PRODUCT_ID);
GO
