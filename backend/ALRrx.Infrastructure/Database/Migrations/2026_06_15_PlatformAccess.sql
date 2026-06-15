-- ============================================================================
-- Migration: Add PlatformAccess column for data-driven platform routing
-- Date: 2026-06-15
-- Description: Adds PlatformAccess ENUM column to alrrx_users; migrates the
--              existing hardcoded access list (SLICE_ONLY, ALTRX_ONLY, BOTH)
--              from accessControl.ts into the database so admins can manage it
--              from the Admin Panel.
-- ============================================================================

SET @fk_check = @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------------------------------------------------------
-- 1. Add the column
-- ----------------------------------------------------------------------------

ALTER TABLE alrrx_users
  ADD COLUMN IF NOT EXISTS PlatformAccess ENUM('None','Altrx','Slice','Both') NOT NULL DEFAULT 'None';

-- ----------------------------------------------------------------------------
-- 2. Migrate existing users from the legacy hardcoded access list
-- ----------------------------------------------------------------------------

-- BOTH group: 4 bootstrap admins
UPDATE alrrx_users SET PlatformAccess = 'Both'
WHERE LOWER(Email) IN (
  'david@revolutionmedia.ai',
  'j.lines@revolutionmedia.ai',
  'cuauhtemoc@revolutionmedia.ai',
  'kevin.escalante@revolutionmedia.ai'
);

-- ALTRX_ONLY group
UPDATE alrrx_users SET PlatformAccess = 'Altrx'
WHERE LOWER(Email) IN (
  'jessica.duarte@revolutionmedia.ai',
  'silverio.arellano@revolutionmedia.ai'
);

-- SLICE_ONLY group
UPDATE alrrx_users SET PlatformAccess = 'Slice'
WHERE LOWER(Email) IN (
  'pedro@revolutionmedia.ai',
  'ofelia.palomino@revolutionmedia.ai',
  'victor.ramirez@revolutionmedia.ai',
  'jose.camacho@revolutionmedia.ai',
  'luis.mariano@revolutionmedia.ai',
  'nayeli.novoa@revolutionmedia.ai',
  'eduardo.hernandez@revolutionmedia.ai',
  'kenny.santaella@revolutionmedia.ai'
);

-- Everyone else stays at the default 'None' (existing behavior — no access)

SET FOREIGN_KEY_CHECKS = @fk_check;

-- ============================================================================
-- End of migration
-- ============================================================================
