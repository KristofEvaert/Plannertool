-- Cleanup script to fix ServiceTypes table
-- This will remove duplicate/incorrect entries and ensure only the correct 3 service types exist
-- NOTE: Must delete ServiceLocations first due to foreign key constraint

-- First, check what we have
SELECT 'Current ServiceTypes:' AS Info;
SELECT * FROM ServiceTypes ORDER BY Id;

SELECT 'Current ServiceLocations count:' AS Info;
SELECT COUNT(*) AS Count FROM ServiceLocations;

-- CRITICAL: Delete ServiceLocations first (due to FK constraint)
DELETE FROM ServiceLocations;
PRINT 'ServiceLocations deleted.';

-- Now we can safely delete service types
-- Delete any service types that don't match the correct codes
DELETE FROM ServiceTypes 
WHERE Code NOT IN ('CHARGING_POST', 'PHARMA', 'GENERAL');

-- If there are duplicates, keep only the first one for each code
-- (This handles the case where CHARGING_POSTS exists alongside CHARGING_POST)
WITH Duplicates AS (
    SELECT Id,
           ROW_NUMBER() OVER (PARTITION BY Code ORDER BY Id) AS RowNum
    FROM ServiceTypes
    WHERE Code IN ('CHARGING_POST', 'PHARMA', 'GENERAL')
)
DELETE FROM ServiceTypes
WHERE Id IN (
    SELECT Id FROM Duplicates WHERE RowNum > 1
);

-- Update any incorrect data
UPDATE ServiceTypes 
SET Code = 'CHARGING_POST',
    Name = 'Charging Posts',
    Description = 'Electric vehicle charging posts'
WHERE Code = 'CHARGING_POSTS' OR (Code = 'CHARGING_POST' AND Name != 'Charging Posts');

-- Verify the final state
SELECT 'Final ServiceTypes:' AS Info;
SELECT * FROM ServiceTypes ORDER BY Id;

-- Expected result: 3 rows
-- 1. CHARGING_POST - Charging Posts
-- 2. PHARMA - Pharmacist Interventions  
-- 3. GENERAL - General Service

-- NOTE: After running this script, restart the application to reseed ServiceLocations

