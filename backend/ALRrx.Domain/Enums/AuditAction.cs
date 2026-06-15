namespace ALRrx.Domain.Enums;

public enum AuditAction
{
    Registered,
    Login,
    LoginFailed,
    Logout,
    Approved,
    Rejected,
    Suspended,
    Reactivated,
    RoleChanged,
    PasswordReset,
    Locked,
    EmailFailed,
    TokenRevoked
}
