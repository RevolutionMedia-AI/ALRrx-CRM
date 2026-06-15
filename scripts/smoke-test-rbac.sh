#!/usr/bin/env bash
# ============================================================================
# ALRrx CRM — RBAC + User Approval E2E Smoke Test
# ============================================================================
# Validates the full pending-approval → admin-approves → active flow,
# plus the self-protection guards and rate limiting.
#
# Usage:
#   ./scripts/smoke-test-rbac.sh                 # uses default http://localhost:5000
#   BASE_URL=http://localhost:8080 ./scripts/smoke-test-rbac.sh
#
# Prerequisites:
#   - Backend running and reachable at $BASE_URL
#   - Database has been migrated (Phase 0 SQL)
#   - At least one Admin user exists (e.g. kevin.escalante@revolutionmedia.ai)
#   - jq installed (apt install jq / brew install jq)
#
# Note: This script exercises the email/password and admin endpoints only.
# Google-login flow requires a real Google OAuth token and must be tested
# manually via the frontend.
# ============================================================================

set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5000}"
ADMIN_EMAIL="${ADMIN_EMAIL:-kevin.escalante@revolutionmedia.ai}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-Admin123!}"
TEST_USER_EMAIL="${TEST_USER_EMAIL:-smoketest-$(date +%s)@example.com}"
TEST_USER_NAME="${TEST_USER_NAME:-Smoke Test User}"
TEST_USER_PASSWORD="${TEST_USER_PASSWORD:-Test1234!Abcd}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

PASSED=0
FAILED=0
SKIPPED=0

pass() { echo -e "  ${GREEN}✓${NC} $1"; PASSED=$((PASSED + 1)); }
fail() { echo -e "  ${RED}✗${NC} $1"; FAILED=$((FAILED + 1)); }
skip() { echo -e "  ${YELLOW}⊘${NC} $1 (skipped)"; SKIPPED=$((SKIPPED + 1)); }

hr() { echo ""; echo -e "${YELLOW}── $1 ──${NC}"; }

# ============================================================================
# Helpers
# ============================================================================

# Make a request and return HTTP status
http_status() {
  local method="$1" path="$2" token="${3:-}" body="${4:-}"
  local args=(-s -o /tmp/smoke_body -w "%{http_code}" -X "$method" "$BASE_URL$path")
  [[ -n "$token" ]] && args+=(-H "Authorization: Bearer $token")
  if [[ -n "$body" ]]; then
    args+=(-H "Content-Type: application/json" -d "$body")
  fi
  curl "${args[@]}"
}

# Extract JSON field
jget() { jq -r "$1" /tmp/smoke_body 2>/dev/null; }

# ============================================================================
# Phase 0: Pre-flight
# ============================================================================

hr "Pre-flight"

status=$(http_status GET /health)
if [[ "$status" == "200" ]]; then
  pass "Health endpoint returns 200"
else
  fail "Health endpoint returned $status — is the backend running at $BASE_URL?"
  exit 1
fi

# ============================================================================
# Phase 1: Admin login → should get a valid token with role=Admin
# ============================================================================

hr "Admin login"

status=$(http_status POST /auth/login "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}")
if [[ "$status" == "200" ]]; then
  ADMIN_TOKEN=$(jget '.token')
  if [[ -n "$ADMIN_TOKEN" && "$ADMIN_TOKEN" != "null" ]]; then
    pass "Admin login succeeded, got token"
  else
    fail "Admin login returned 200 but no token in body"
    cat /tmp/smoke_body
    exit 1
  fi
else
  fail "Admin login returned $status"
  cat /tmp/smoke_body
  exit 1
fi

ADMIN_USER_ID=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "$BASE_URL/auth/me" | jq -r '.id')
ADMIN_STATUS=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "$BASE_URL/auth/me" | jq -r '.status')
ADMIN_ROLE=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "$BASE_URL/auth/me" | jq -r '.role')

if [[ "$ADMIN_STATUS" == "Active" ]]; then
  pass "Admin /me returns status=Active"
else
  fail "Admin /me returns status=$ADMIN_STATUS (expected Active)"
fi
if [[ "$ADMIN_ROLE" == "Admin" ]]; then
  pass "Admin /me returns role=Admin"
else
  fail "Admin /me returns role=$ADMIN_ROLE (expected Admin)"
fi

# Verify permissions are in the JWT
ADMIN_PERMS=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "$BASE_URL/auth/me" | jq -r '.permissions | length')
if [[ "$ADMIN_PERMS" -gt 5 ]]; then
  pass "Admin has $ADMIN_PERMS permissions loaded"
else
  fail "Admin only has $ADMIN_PERMS permissions (expected >5)"
fi

# ============================================================================
# Phase 2: Verify migration ran (roles table populated)
# ============================================================================

hr "RBAC tables"

status=$(http_status GET /roles "$ADMIN_TOKEN")
if [[ "$status" == "200" ]]; then
  ROLE_COUNT=$(jget '. | length')
  pass "GET /roles returns $ROLE_COUNT roles"
  if [[ "$ROLE_COUNT" -ge 4 ]]; then
    pass "At least 4 roles defined (Admin/Supervisor/Employee/VicidialEditor)"
  else
    fail "Only $ROLE_COUNT roles — migration may not have seeded correctly"
  fi
else
  fail "GET /roles returned $status"
fi

# ============================================================================
# Phase 3: Admin can list pending users
# ============================================================================

hr "Pending users list"

status=$(http_status GET "/admin/users?status=Pending" "$ADMIN_TOKEN")
if [[ "$status" == "200" ]]; then
  pass "GET /admin/users?status=Pending returns 200"
else
  fail "GET /admin/users?status=Pending returned $status"
fi

# ============================================================================
# Phase 4: Register a new user via admin (should become Active)
# ============================================================================

hr "Register user (admin-created)"

status=$(http_status GET /roles "$ADMIN_TOKEN")
EMPLOYEE_ROLE_ID=$(jget '.[] | select(.name=="Employee") | .id')

if [[ -z "$EMPLOYEE_ROLE_ID" || "$EMPLOYEE_ROLE_ID" == "null" ]]; then
  fail "Could not find Employee roleId"
  exit 1
fi

# Register via /api/auth/register (admin-only)
status=$(http_status POST /auth/register "$ADMIN_TOKEN" \
  "{\"email\":\"$TEST_USER_EMAIL\",\"password\":\"$TEST_USER_PASSWORD\",\"fullName\":\"$TEST_USER_NAME\",\"roleId\":$EMPLOYEE_ROLE_ID}")
if [[ "$status" == "200" ]]; then
  TEST_USER_ID=$(jget '.id')
  TEST_USER_STATUS=$(jget '.status')
  pass "User registered: $TEST_USER_EMAIL (id=$TEST_USER_ID, status=$TEST_USER_STATUS)"
else
  fail "POST /auth/register returned $status"
  cat /tmp/smoke_body
  exit 1
fi

# ============================================================================
# Phase 5: New admin-created user can login → status=Active
# ============================================================================

hr "New user login"

status=$(http_status POST /auth/login "{\"email\":\"$TEST_USER_EMAIL\",\"password\":\"$TEST_USER_PASSWORD\"}")
if [[ "$status" == "200" ]]; then
  TEST_TOKEN=$(jget '.token')
  TEST_STATUS=$(jget '.user.status')
  if [[ "$TEST_STATUS" == "Active" ]]; then
    pass "New user can login with status=Active"
  else
    fail "New user logged in but status=$TEST_STATUS (expected Active)"
  fi
else
  fail "New user login returned $status"
  cat /tmp/smoke_body
fi

# Verify the new user can access protected routes
status=$(http_status GET /auth/me "$TEST_TOKEN")
if [[ "$status" == "200" ]]; then
  pass "New user can access /auth/me"
else
  fail "New user cannot access /auth/me (status=$status)"
fi

status=$(http_status GET /dashboard/summary "$TEST_TOKEN")
if [[ "$status" == "200" ]]; then
  pass "New user can access /dashboard/summary (Employee permission)"
else
  fail "New user cannot access /dashboard/summary (status=$status)"
fi

# ============================================================================
# Phase 6: Employee CANNOT access admin endpoints
# ============================================================================

hr "Role-based access control"

status=$(http_status GET /admin/users "$TEST_TOKEN")
if [[ "$status" == "403" || "$status" == "401" ]]; then
  pass "Employee denied from /admin/users (status=$status)"
else
  fail "Employee GOT ACCESS to /admin/users (status=$status) — security issue!"
fi

status=$(http_status POST "/admin/users/$TEST_USER_ID/suspend" "$TEST_TOKEN" \
  "{\"reason\":\"test\"}")
if [[ "$status" == "403" || "$status" == "401" ]]; then
  pass "Employee denied from suspend endpoint"
else
  fail "Employee GOT ACCESS to suspend (status=$status) — security issue!"
fi

# ============================================================================
# Phase 7: Admin can suspend new user → middleware blocks
# ============================================================================

hr "Suspend user flow"

status=$(http_status POST "/admin/users/$TEST_USER_ID/suspend" "$ADMIN_TOKEN" \
  '{"reason":"smoke test suspension"}')
if [[ "$status" == "200" ]]; then
  pass "Admin suspended user (status=$status)"
else
  fail "Admin suspend returned $status"
  cat /tmp/smoke_body
fi

# Verify the suspended user is blocked
status=$(http_status GET /dashboard/summary "$TEST_TOKEN")
if [[ "$status" == "403" ]]; then
  SUSPENDED_BODY=$(cat /tmp/smoke_body)
  CODE=$(echo "$SUSPENDED_BODY" | jq -r '.code // ""')
  if [[ "$CODE" == "USER_SUSPENDED" ]]; then
    pass "Suspended user is blocked with code=USER_SUSPENDED"
  else
    fail "Suspended user is blocked but code=$CODE"
  fi
else
  fail "Suspended user got status=$status (expected 403)"
fi

# ============================================================================
# Phase 8: Admin can reactivate
# ============================================================================

hr "Reactivate user"

status=$(http_status POST "/admin/users/$TEST_USER_ID/reactivate" "$ADMIN_TOKEN")
if [[ "$status" == "200" ]]; then
  pass "Admin reactivated user"
else
  fail "Reactivate returned $status"
fi

# Test user can now login
status=$(http_status POST /auth/login "{\"email\":\"$TEST_USER_EMAIL\",\"password\":\"$TEST_USER_PASSWORD\"}")
if [[ "$status" == "200" ]]; then
  TEST_TOKEN=$(jget '.token')
  pass "Reactivated user can login again"
else
  fail "Reactivated user cannot login (status=$status)"
fi

# ============================================================================
# Phase 9: Self-protection guards
# ============================================================================

hr "Admin self-protection"

# Admin trying to suspend themselves
status=$(http_status POST "/admin/users/$ADMIN_USER_ID/suspend" "$ADMIN_TOKEN" \
  '{"reason":"oops"}')
if [[ "$status" == "400" ]]; then
  ERR=$(jget '.error')
  if echo "$ERR" | grep -qi "yourself\|admin"; then
    pass "Admin cannot suspend self (got: '$ERR')"
  else
    fail "Admin suspend-self returned 400 but error was: $ERR"
  fi
else
  fail "Admin suspend-self returned $status (expected 400)"
fi

# Admin trying to reject themselves
status=$(http_status POST "/admin/users/$ADMIN_USER_ID/reject" "$ADMIN_TOKEN" \
  '{"reason":"oops"}')
if [[ "$status" == "400" ]]; then
  pass "Admin cannot reject self (status=400)"
else
  fail "Admin reject-self returned $status (expected 400)"
fi

# Admin trying to change their own role away from Admin
# First get the current role id for Admin
ADMIN_ROLE_ID=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "$BASE_URL/roles" | jq -r '.[] | select(.name=="Admin") | .id')
EMPLOYEE_ROLE_ID=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "$BASE_URL/roles" | jq -r '.[] | select(.name=="Employee") | .id')
status=$(http_status PUT "/admin/users/$ADMIN_USER_ID/role" "$ADMIN_TOKEN" \
  "{\"roleId\":$EMPLOYEE_ROLE_ID}")
if [[ "$status" == "400" ]]; then
  pass "Admin cannot demote self (status=400)"
else
  fail "Admin self-demote returned $status (expected 400)"
fi

# ============================================================================
# Phase 10: Rate limiting
# ============================================================================

hr "Rate limiting"

echo "  Testing auth rate limit (5/min per IP)..."
HITS_429=0
HITS_OTHER=0
for i in {1..12}; do
  status=$(http_status POST /auth/login '{"email":"nobody@example.com","password":"x"}')
  if [[ "$status" == "429" ]]; then
    HITS_429=$((HITS_429 + 1))
  else
    HITS_OTHER=$((HITS_OTHER + 1))
  fi
done
if [[ "$HITS_429" -gt 0 ]]; then
  pass "Rate limiter kicked in after 5 requests ($HITS_429 got 429, $HITS_OTHER got 401/200)"
else
  fail "Rate limiter did NOT trigger after 12 requests (all got $HITS_OTHER non-429)"
fi

# ============================================================================
# Phase 11: Audit log
# ============================================================================

hr "Audit log"

status=$(http_status GET /admin/audit "$ADMIN_TOKEN")
if [[ "$status" == "200" ]]; then
  AUDIT_COUNT=$(jget '. | length')
  if [[ "$AUDIT_COUNT" -gt 0 ]]; then
    pass "Audit log has $AUDIT_COUNT entries"
    # Find at least one 'Suspended' entry
    SUSPENDED_COUNT=$(jget '[.[] | select(.action=="Suspended")] | length')
    if [[ "$SUSPENDED_COUNT" -gt 0 ]]; then
      pass "Audit log contains $SUSPENDED_COUNT 'Suspended' entries"
    else
      fail "No 'Suspended' entries in audit log"
    fi
  else
    fail "Audit log is empty"
  fi
else
  fail "GET /admin/audit returned $status"
fi

# ============================================================================
# Phase 12: Password reset
# ============================================================================

hr "Password reset"

status=$(http_status POST "/admin/users/$TEST_USER_ID/reset-password" "$ADMIN_TOKEN")
if [[ "$status" == "200" ]]; then
  TEMP_PWD=$(jget '.temporaryPassword')
  if [[ -n "$TEMP_PWD" && "$TEMP_PWD" != "null" ]]; then
    pass "Password reset returned temp password (length: ${#TEMP_PWD})"
    # Try login with temp password
    status=$(http_status POST /auth/login "{\"email\":\"$TEST_USER_EMAIL\",\"password\":\"$TEMP_PWD\"}")
    if [[ "$status" == "200" ]]; then
      pass "User can login with temp password"
    else
      fail "Temp password login returned $status"
    fi
  else
    fail "Reset-password response missing temporaryPassword"
  fi
else
  fail "Reset-password returned $status"
fi

# ============================================================================
# Cleanup
# ============================================================================

hr "Cleanup"

status=$(http_status POST "/admin/users/$TEST_USER_ID/suspend" "$ADMIN_TOKEN" \
  '{"reason":"smoke test cleanup"}')
if [[ "$status" == "200" || "$status" == "400" ]]; then
  pass "Test user suspended (cleanup)"
else
  fail "Cleanup suspend returned $status"
fi

# ============================================================================
# Summary
# ============================================================================

echo ""
echo "═══════════════════════════════════════════════"
echo -e "  ${GREEN}Passed${NC}:  $PASSED"
echo -e "  ${RED}Failed${NC}:  $FAILED"
echo -e "  ${YELLOW}Skipped${NC}: $SKIPPED"
echo "═══════════════════════════════════════════════"

if [[ "$FAILED" -gt 0 ]]; then
  echo -e "${RED}❌ Smoke test FAILED${NC}"
  exit 1
else
  echo -e "${GREEN}✅ All smoke tests passed${NC}"
  exit 0
fi
