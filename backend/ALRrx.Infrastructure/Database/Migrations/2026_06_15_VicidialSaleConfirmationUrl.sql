-- ============================================================================
-- Migration: Add ConfirmationUrl column to vicidial_form_sales
-- Date: 2026-06-15
-- Description: Adds a nullable ConfirmationUrl column to vicidial_form_sales
--              so that newly registered sales can store the URL provided in
--              the purchase confirmation. Existing sales (registered before
--              this change) are intentionally left as NULL — the field is
--              only required for sales registered after this migration runs.
-- ============================================================================

SET @fk_check = @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------------------------------------------------------
-- 1. Add the column (idempotent, safe to re-run)
-- ----------------------------------------------------------------------------

ALTER TABLE vicidial_form_sales
  ADD COLUMN IF NOT EXISTS ConfirmationUrl VARCHAR(2048) NULL AFTER Amount;

-- ----------------------------------------------------------------------------
-- 2. Backfill: leave existing rows as NULL on purpose. The new column is
--    optional for legacy data and required only for new inserts going
--    forward (enforced at the application layer in the use case).
-- ----------------------------------------------------------------------------

-- (no UPDATE statements)

SET FOREIGN_KEY_CHECKS = @fk_check;

-- ============================================================================
-- End of migration
-- ============================================================================
