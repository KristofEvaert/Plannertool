-- Script to ensure ServiceTypeId column exists in ServiceLocations table
-- Run this in SQL Server Management Studio against your TransportPlanner database

-- Check if column exists
IF NOT EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'ServiceLocations' 
    AND COLUMN_NAME = 'ServiceTypeId'
)
BEGIN
    PRINT 'ServiceTypeId column does not exist. Adding it...';
    
    -- Add the column
    ALTER TABLE ServiceLocations
    ADD ServiceTypeId INT NOT NULL DEFAULT 1;
    
    PRINT 'ServiceTypeId column added successfully.';
END
ELSE
BEGIN
    PRINT 'ServiceTypeId column already exists.';
END

-- Check if ServiceTypes table exists, if not create it
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ServiceTypes')
BEGIN
    PRINT 'ServiceTypes table does not exist. Creating it...';
    
    CREATE TABLE ServiceTypes (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Code NVARCHAR(50) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        Description NVARCHAR(1000) NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAtUtc DATETIME2 NOT NULL,
        UpdatedAtUtc DATETIME2 NOT NULL
    );
    
    CREATE UNIQUE INDEX IX_ServiceTypes_Code ON ServiceTypes(Code);
    
    -- Insert default service types
    DECLARE @now DATETIME2 = GETUTCDATE();
    INSERT INTO ServiceTypes (Code, Name, Description, IsActive, CreatedAtUtc, UpdatedAtUtc)
    VALUES 
        ('CHARGING_POST', 'Charging Posts', 'Electric vehicle charging posts', 1, @now, @now),
        ('PHARMA', 'Pharmacist Interventions', 'Pharmacist service interventions', 1, @now, @now),
        ('GENERAL', 'General Service', 'General service locations', 1, @now, @now);
    
    PRINT 'ServiceTypes table created and seeded.';
END
ELSE
BEGIN
    PRINT 'ServiceTypes table already exists.';
END

-- Check if foreign key exists, if not create it
IF NOT EXISTS (
    SELECT 1 
    FROM sys.foreign_keys 
    WHERE name = 'FK_ServiceLocations_ServiceTypes_ServiceTypeId'
)
BEGIN
    PRINT 'Foreign key does not exist. Creating it...';
    
    -- Ensure ServiceTypeId values are valid (set to 1 if invalid)
    UPDATE ServiceLocations
    SET ServiceTypeId = 1
    WHERE ServiceTypeId NOT IN (SELECT Id FROM ServiceTypes) OR ServiceTypeId = 0;
    
    -- Add foreign key
    ALTER TABLE ServiceLocations
    ADD CONSTRAINT FK_ServiceLocations_ServiceTypes_ServiceTypeId
    FOREIGN KEY (ServiceTypeId) REFERENCES ServiceTypes(Id);
    
    PRINT 'Foreign key created successfully.';
END
ELSE
BEGIN
    PRINT 'Foreign key already exists.';
END

-- Check if index exists, if not create it
IF NOT EXISTS (
    SELECT 1 
    FROM sys.indexes 
    WHERE name = 'IX_ServiceLocations_ServiceTypeId_Status_DueDate'
)
BEGIN
    PRINT 'Index does not exist. Creating it...';
    
    CREATE INDEX IX_ServiceLocations_ServiceTypeId_Status_DueDate
    ON ServiceLocations(ServiceTypeId, Status, DueDate);
    
    PRINT 'Index created successfully.';
END
ELSE
BEGIN
    PRINT 'Index already exists.';
END

-- Verify the column exists and show current values
SELECT 
    'ServiceTypeId Column Check' AS CheckType,
    CASE 
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ServiceLocations' AND COLUMN_NAME = 'ServiceTypeId')
        THEN 'EXISTS'
        ELSE 'MISSING'
    END AS Status;

SELECT 
    Id,
    ErpId,
    Name,
    ServiceTypeId,
    (SELECT Name FROM ServiceTypes WHERE Id = ServiceLocations.ServiceTypeId) AS ServiceTypeName
FROM ServiceLocations
ORDER BY Id;

