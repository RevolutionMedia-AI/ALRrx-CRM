-- ============================================================================
-- Migration: Add RBAC + User Approval workflow
-- Date: 2026-06-12
-- Description: Adds roles, permissions, role_permissions, user_audit_log tables;
--              adds Status, RoleId, approval/lockout fields to alrrx_users;
--              seeds 4 roles + 15 permissions; auto-approves existing users.
-- ============================================================================

SET @fk_check = @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------------------------------------------------------
-- 1. Create new RBAC tables
-- ----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS alrrx_roles (
  Id INT AUTO_INCREMENT PRIMARY KEY,
  Name VARCHAR(50) NOT NULL UNIQUE,
  Description VARCHAR(255) NULL,
  IsSystem TINYINT(1) NOT NULL DEFAULT 0,
  CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS alrrx_permissions (
  Id INT AUTO_INCREMENT PRIMARY KEY,
  KeyName VARCHAR(100) NOT NULL UNIQUE,
  Description VARCHAR(255) NULL,
  Module VARCHAR(50) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS alrrx_role_permissions (
  RoleId INT NOT NULL,
  PermissionId INT NOT NULL,
  PRIMARY KEY (RoleId, PermissionId),
  CONSTRAINT FK_rp_role FOREIGN KEY (RoleId) REFERENCES alrrx_roles(Id) ON DELETE CASCADE,
  CONSTRAINT FK_rp_perm FOREIGN KEY (PermissionId) REFERENCES alrrx_permissions(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS alrrx_user_audit_log (
  Id BIGINT AUTO_INCREMENT PRIMARY KEY,
  UserId INT NOT NULL,
  Action VARCHAR(50) NOT NULL,
  PerformedBy INT NULL,
  OldValue VARCHAR(255) NULL,
  NewValue VARCHAR(255) NULL,
  Reason VARCHAR(500) NULL,
  IpAddress VARCHAR(45) NULL,
  CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT FK_audit_user FOREIGN KEY (UserId) REFERENCES alrrx_users(Id),
  CONSTRAINT FK_audit_performedBy FOREIGN KEY (PerformedBy) REFERENCES alrrx_users(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE INDEX IX_audit_user_created ON alrrx_user_audit_log(UserId, CreatedAt);
CREATE INDEX IX_audit_action ON alrrx_user_audit_log(Action);

-- ----------------------------------------------------------------------------
-- 2. Seed roles
-- ----------------------------------------------------------------------------

INSERT INTO alrrx_roles (Name, Description, IsSystem) VALUES
  ('Admin',          'Full system access',              1),
  ('Supervisor',     'Team management + read all',      1),
  ('Employee',       'Read-only basic access',          1),
  ('VicidialEditor', 'Can edit Vicidial sales entries', 1)
ON DUPLICATE KEY UPDATE Description = VALUES(Description);

-- ----------------------------------------------------------------------------
-- 3. Seed permissions
-- ----------------------------------------------------------------------------

INSERT INTO alrrx_permissions (KeyName, Description, Module) VALUES
  ('users.view',         'View users list',                       'users'),
  ('users.approve',      'Approve pending users',                'users'),
  ('users.edit',         'Edit user details and roles',          'users'),
  ('users.suspend',      'Suspend/reactivate users',             'users'),
  ('admin.view',         'Access admin panel',                   'admin'),
  ('dashboard.view',     'View dashboards',                      'dashboard'),
  ('reports.view',       'View reports',                         'reports'),
  ('staffing.view',      'View staffing',                        'staffing'),
  ('staffing.view.team', 'View team staffing (Supervisor only)', 'staffing'),
  ('twilio.view',        'View Twilio costs',                    'twilio'),
  ('vicidial.view',      'View Vicidial sales',                  'vicidial'),
  ('vicidial.edit',      'Edit Vicidial sales',                  'vicidial'),
  ('period-comparison.run', 'Run period comparison exports',     'reports'),
  ('data.edit',          'Edit CRM data rows',                   'data'),
  ('data.delete',        'Delete CRM data rows',                 'data')
ON DUPLICATE KEY UPDATE Description = VALUES(Description), Module = VALUES(Module);

-- ----------------------------------------------------------------------------
-- 4. Seed role <-> permission mappings
-- ----------------------------------------------------------------------------
-- Admin gets ALL permissions
INSERT IGNORE INTO alrrx_role_permissions (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM alrrx_roles r
CROSS JOIN alrrx_permissions p
WHERE r.Name = 'Admin';

-- Supervisor: read all + manage team
INSERT IGNORE INTO alrrx_role_permissions (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM alrrx_roles r
JOIN alrrx_permissions p ON p.KeyName IN (
  'users.view',
  'dashboard.view',
  'reports.view',
  'staffing.view',
  'staffing.view.team',
  'twilio.view',
  'vicidial.view',
  'period-comparison.run'
)
WHERE r.Name = 'Supervisor';

-- Employee: basic read
INSERT IGNORE INTO alrrx_role_permissions (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM alrrx_roles r
JOIN alrrx_permissions p ON p.KeyName IN (
  'dashboard.view',
  'reports.view',
  'vicidial.view'
)
WHERE r.Name = 'Employee';

-- VicidialEditor: vicidial only
INSERT IGNORE INTO alrrx_role_permissions (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM alrrx_roles r
JOIN alrrx_permissions p ON p.KeyName IN (
  'vicidial.view',
  'vicidial.edit'
)
WHERE r.Name = 'VicidialEditor';

-- ----------------------------------------------------------------------------
-- 5. Modify alrrx_users: add new columns
-- ----------------------------------------------------------------------------

-- Add Status column (default 'Active' to preserve existing behavior)
ALTER TABLE alrrx_users
  ADD COLUMN IF NOT EXISTS Status ENUM('Pending','Active','Rejected','Suspended') NOT NULL DEFAULT 'Active',
  ADD COLUMN IF NOT EXISTS RoleId INT NULL,
  ADD COLUMN IF NOT EXISTS ApprovedBy INT NULL,
  ADD COLUMN IF NOT EXISTS ApprovedAt DATETIME NULL,
  ADD COLUMN IF NOT EXISTS RejectionReason VARCHAR(500) NULL,
  ADD COLUMN IF NOT EXISTS LastLoginAt DATETIME NULL,
  ADD COLUMN IF NOT EXISTS FailedLoginAttempts INT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS LockedUntil DATETIME NULL;

-- Add index on Status
SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'alrrx_users'
                      AND INDEX_NAME = 'IX_alrrx_users_Status');
SET @sql := IF(@idx_exists = 0,
               'CREATE INDEX IX_alrrx_users_Status ON alrrx_users(Status)',
               'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ----------------------------------------------------------------------------
-- 6. Migrate existing data: map Role VARCHAR -> RoleId FK
-- ----------------------------------------------------------------------------

UPDATE alrrx_users u
JOIN alrrx_roles r ON r.Name = u.Role
SET u.RoleId = r.Id
WHERE u.RoleId IS NULL;

-- ----------------------------------------------------------------------------
-- 7. Auto-approve existing users based on IsActive
-- ----------------------------------------------------------------------------

UPDATE alrrx_users
SET Status = CASE
  WHEN IsActive = 1 THEN 'Active'
  ELSE 'Suspended'
END,
ApprovedAt = COALESCE(ApprovedAt, CreatedAt)
WHERE Status = 'Active' AND ApprovedAt IS NULL;

-- ----------------------------------------------------------------------------
-- 8. Add FK constraints (deferred until after data migration)
-- ----------------------------------------------------------------------------

-- FK alrrx_users.RoleId -> alrrx_roles.Id
SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
                   WHERE TABLE_SCHEMA = DATABASE()
                     AND TABLE_NAME = 'alrrx_users'
                     AND CONSTRAINT_NAME = 'FK_alrrx_users_RoleId');
SET @sql := IF(@fk_exists = 0,
  'ALTER TABLE alrrx_users ADD CONSTRAINT FK_alrrx_users_RoleId FOREIGN KEY (RoleId) REFERENCES alrrx_roles(Id)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- FK alrrx_users.ApprovedBy -> alrrx_users.Id
SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
                   WHERE TABLE_SCHEMA = DATABASE()
                     AND TABLE_NAME = 'alrrx_users'
                     AND CONSTRAINT_NAME = 'FK_alrrx_users_ApprovedBy');
SET @sql := IF(@fk_exists = 0,
  'ALTER TABLE alrrx_users ADD CONSTRAINT FK_alrrx_users_ApprovedBy FOREIGN KEY (ApprovedBy) REFERENCES alrrx_users(Id)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ----------------------------------------------------------------------------
-- 9. Make RoleId NOT NULL now that all rows have it
-- ----------------------------------------------------------------------------

-- Guard: only flip to NOT NULL if no NULLs remain
SET @nulls := (SELECT COUNT(*) FROM alrrx_users WHERE RoleId IS NULL);
SET @sql := IF(@nulls = 0,
  'ALTER TABLE alrrx_users MODIFY COLUMN RoleId INT NOT NULL',
  'SELECT ''WARNING: alrrx_users.RoleId still has NULL values - skipping NOT NULL constraint'' AS warning');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET FOREIGN_KEY_CHECKS = @fk_check;

-- ============================================================================
-- End of migration
-- ============================================================================
