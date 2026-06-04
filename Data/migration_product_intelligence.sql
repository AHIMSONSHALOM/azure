-- SQL Migration Script: Product Intelligence Platform Database Changes

-- 1. Add F_SUBCATEGORY column to T_PRODUCTS table if not exists
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('[dbo].[T_PRODUCTS]') AND name = 'F_SUBCATEGORY')
BEGIN
    ALTER TABLE [dbo].[T_PRODUCTS] ADD [F_SUBCATEGORY] NVARCHAR(100) NULL;
END
GO

-- 2. Create T_PRODUCT_IMAGES table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_IMAGES]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[T_PRODUCT_IMAGES] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [ProductId] INT NOT NULL,
        [ImageUrl] NVARCHAR(MAX) NOT NULL,
        [Source] NVARCHAR(255) NULL,
        [IsPrimary] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT [FK_T_PRODUCT_IMAGES_T_PRODUCTS] FOREIGN KEY ([ProductId]) 
            REFERENCES [dbo].[T_PRODUCTS] ([F_PRODUCT_ID]) ON DELETE CASCADE
    );
END
GO

-- 3. Create T_PRODUCT_SOURCES table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_SOURCES]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[T_PRODUCT_SOURCES] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [ProductId] INT NOT NULL,
        [SourceUrl] NVARCHAR(MAX) NOT NULL,
        [SourceName] NVARCHAR(255) NOT NULL,
        CONSTRAINT [FK_T_PRODUCT_SOURCES_T_PRODUCTS] FOREIGN KEY ([ProductId]) 
            REFERENCES [dbo].[T_PRODUCTS] ([F_PRODUCT_ID]) ON DELETE CASCADE
    );
END
GO

-- 4. Create T_PRODUCT_NEWS table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_NEWS]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[T_PRODUCT_NEWS] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [ProductId] INT NOT NULL,
        [Title] NVARCHAR(500) NOT NULL,
        [Url] NVARCHAR(MAX) NOT NULL,
        [Summary] NVARCHAR(MAX) NULL,
        [PublishDate] DATETIME NULL,
        CONSTRAINT [FK_T_PRODUCT_NEWS_T_PRODUCTS] FOREIGN KEY ([ProductId]) 
            REFERENCES [dbo].[T_PRODUCTS] ([F_PRODUCT_ID]) ON DELETE CASCADE
    );
END
GO

-- 5. Create T_PRODUCT_ANALYTICS table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[T_PRODUCT_ANALYTICS]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[T_PRODUCT_ANALYTICS] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [ProductId] INT NULL,
        [SearchQuery] NVARCHAR(500) NULL,
        [Timestamp] DATETIME NOT NULL DEFAULT GETDATE(),
        [UserSession] NVARCHAR(255) NULL,
        CONSTRAINT [FK_T_PRODUCT_ANALYTICS_T_PRODUCTS] FOREIGN KEY ([ProductId]) 
            REFERENCES [dbo].[T_PRODUCTS] ([F_PRODUCT_ID]) ON DELETE SET NULL
    );
END
GO
