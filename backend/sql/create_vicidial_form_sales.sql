CREATE TABLE IF NOT EXISTS vicidial_form_sales (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SalesRep VARCHAR(100) NOT NULL,
    SaleDate DATETIME NOT NULL,
    ClientPhone VARCHAR(50) NOT NULL,
    ClientName VARCHAR(255) NOT NULL,
    ClientEmail VARCHAR(255) NOT NULL,
    Bundle VARCHAR(30) NOT NULL,
    Amount DECIMAL(10,2) NOT NULL,
    Source VARCHAR(20) NOT NULL DEFAULT 'VicidialForm',
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_rep_date (SalesRep, SaleDate),
    INDEX idx_date (SaleDate)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
