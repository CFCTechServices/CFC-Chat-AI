(() => {
  const { Card, PrimaryButton, TextInput } = window.CFC.Primitives;
  const { Toggle } = window.CFC.Admin;
  const { useUser } = window.CFC.UserContext;
  const { useState, useEffect, useCallback } = React;

  const ROLES = ['user', 'dev', 'admin'];

  function UsersTab() {
    const { session } = useUser();
    const [users, setUsers] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [actionError, setActionError] = useState(null);

    // Invite modal state
    const [showInvite, setShowInvite] = useState(false);
    const [inviteEmail, setInviteEmail] = useState('');
    const [inviteStatus, setInviteStatus] = useState(null);
    const [inviting, setInviting] = useState(false);

    // Delete confirmation state
    const [deleteTarget, setDeleteTarget] = useState(null);

    const authHeaders = useCallback(() => ({
      'Authorization': `Bearer ${session?.access_token}`,
      'Content-Type': 'application/json',
    }), [session]);

    const fetchUsers = useCallback(async () => {
      if (!session) return;
      setLoading(true);
      setError(null);
      try {
        const res = await fetch('/api/admin/users', {
          headers: authHeaders(),
        });
        if (!res.ok) {
          const data = await res.json().catch(() => ({}));
          throw new Error(data.detail || `Failed to fetch users (${res.status})`);
        }
        const data = await res.json();
        setUsers(data.users || []);
      } catch (e) {
        setError(e.message);
      } finally {
        setLoading(false);
      }
    }, [session, authHeaders]);

    useEffect(() => {
      fetchUsers();
    }, [fetchUsers]);

    const handleInvite = async (e) => {
      e.preventDefault();
      if (!inviteEmail.trim()) return;
      setInviting(true);
      setInviteStatus(null);
      try {
        const res = await fetch('/api/admin/invite', {
          method: 'POST',
          headers: authHeaders(),
          body: JSON.stringify({ email: inviteEmail.trim() }),
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.detail || 'Failed to send invite');
        setInviteStatus({ type: 'success', message: data.message });
        setInviteEmail('');
      } catch (e) {
        setInviteStatus({ type: 'error', message: e.message });
      } finally {
        setInviting(false);
      }
    };

    const handleToggleSuspend = async (user) => {
      setActionError(null);
      const isActive = user.status === 'active';
      const endpoint = isActive
        ? `/api/admin/users/${user.id}/deactivate`
        : `/api/admin/users/${user.id}/reactivate`;
      try {
        const res = await fetch(endpoint, {
          method: 'POST',
          headers: authHeaders(),
          body: JSON.stringify({}),
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.detail || 'Action failed');
        fetchUsers();
      } catch (e) {
        setActionError(e.message);
      }
    };

    const handleChangeRole = async (userId, newRole) => {
      setActionError(null);
      try {
        const res = await fetch('/api/admin/change-role', {
          method: 'POST',
          headers: authHeaders(),
          body: JSON.stringify({ user_id: userId, new_role: newRole }),
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.detail || 'Failed to change role');
        fetchUsers();
      } catch (e) {
        setActionError(e.message);
      }
    };

    const handleDelete = async (userId) => {
      setActionError(null);
      try {
        const res = await fetch(`/api/admin/users/${userId}`, {
          method: 'DELETE',
          headers: authHeaders(),
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.detail || 'Failed to delete user');
        setDeleteTarget(null);
        fetchUsers();
      } catch (e) {
        setActionError(e.message);
      }
    };

    const getInitial = (user) => {
      if (user.full_name) return user.full_name.charAt(0).toUpperCase();
      if (user.email) return user.email.charAt(0).toUpperCase();
      return '?';
    };

    const getDisplayName = (user) => {
      return user.full_name || user.email.split('@')[0];
    };

    const getStatusClass = (status) => {
      if (status === 'active') return 'status-active';
      if (status === 'inactive') return 'status-suspended';
      return 'status-pending';
    };

    return (
      <div className="tab-content">
        <div className="tab-header">
          <div>
            <h2>User Management</h2>
            <p className="muted">Invite and manage user access</p>
          </div>
          <PrimaryButton type="button" onClick={() => { setShowInvite(true); setInviteStatus(null); }}>
            <span style={{ marginRight: '0.5rem' }}>+</span>
            Invite User
          </PrimaryButton>
        </div>

        {/* Invite Modal */}
        {showInvite && (
          <Card style={{ marginBottom: '20px', padding: '20px' }}>
            <h3 style={{ marginBottom: '12px' }}>Invite New User</h3>
            <form onSubmit={handleInvite} style={{ display: 'flex', gap: '10px', alignItems: 'flex-end' }}>
              <div style={{ flex: 1 }}>
                <TextInput
                  label="Email Address"
                  type="email"
                  value={inviteEmail}
                  onChange={(e) => setInviteEmail(e.target.value)}
                  placeholder="user@example.com"
                />
              </div>
              <PrimaryButton type="submit" disabled={inviting} style={{ marginBottom: '0' }}>
                {inviting ? 'Inviting...' : 'Invite'}
              </PrimaryButton>
              <button type="button" className="btn-secondary" onClick={() => setShowInvite(false)} style={{ marginBottom: '0' }}>
                Cancel
              </button>
            </form>
            {inviteStatus && (
              <div style={{
                marginTop: '10px',
                padding: '8px 12px',
                borderRadius: '6px',
                backgroundColor: inviteStatus.type === 'success' ? 'var(--color-success-bg, #f0fdf4)' : 'var(--color-error-bg, #fef2f2)',
                color: inviteStatus.type === 'success' ? 'var(--color-success, #16a34a)' : 'var(--color-error, #dc2626)',
                fontSize: '0.9rem',
              }}>
                {inviteStatus.message}
              </div>
            )}
          </Card>
        )}

        {/* Error banners */}
        {error && (
          <div style={{ padding: '10px 14px', borderRadius: '8px', backgroundColor: '#fef2f2', color: '#dc2626', marginBottom: '16px', fontSize: '0.9rem' }}>
            {error}
          </div>
        )}
        {actionError && (
          <div style={{ padding: '10px 14px', borderRadius: '8px', backgroundColor: '#fef2f2', color: '#dc2626', marginBottom: '16px', fontSize: '0.9rem' }}>
            {actionError}
          </div>
        )}

        {/* Delete Confirmation */}
        {deleteTarget && (
          <Card style={{ marginBottom: '16px', padding: '16px', border: '1px solid #dc2626' }}>
            <p style={{ marginBottom: '12px' }}>
              Are you sure you want to <strong>permanently delete</strong> {deleteTarget.email}? This cannot be undone.
            </p>
            <div style={{ display: 'flex', gap: '10px' }}>
              <button
                className="btn-secondary"
                style={{ backgroundColor: '#dc2626', color: 'white', border: 'none' }}
                onClick={() => handleDelete(deleteTarget.id)}
              >
                Delete Permanently
              </button>
              <button className="btn-secondary" onClick={() => setDeleteTarget(null)}>Cancel</button>
            </div>
          </Card>
        )}

        {/* Loading state */}
        {loading && (
          <div style={{ textAlign: 'center', padding: '40px', color: 'var(--color-text-muted)' }}>
            Loading users...
          </div>
        )}

        {/* User list */}
        {!loading && (
          <div className="user-list">
            {users.map(u => (
              <Card key={u.id} className="user-card">
                <div className="user-avatar">{getInitial(u)}</div>
                <div className="user-info">
                  <div className="user-name-row">
                    <h3 className="user-name">{getDisplayName(u)}</h3>
                    <span className={`status-badge ${getStatusClass(u.status)}`}>{u.status}</span>
                    <select
                      value={u.role}
                      onChange={(e) => handleChangeRole(u.id, e.target.value)}
                      style={{
                        padding: '2px 6px',
                        borderRadius: '4px',
                        border: '1px solid var(--color-border)',
                        fontSize: '0.8rem',
                        backgroundColor: 'var(--color-surface)',
                        color: 'var(--color-text)',
                        cursor: 'pointer',
                      }}
                    >
                      {ROLES.map(r => (
                        <option key={r} value={r}>{r}</option>
                      ))}
                    </select>
                  </div>
                  <div className="user-email">{u.email}</div>
                  <div className="user-last-active">
                    Joined: {new Date(u.created_at).toLocaleDateString()}
                    {u.deleted_at && ` | Deactivated: ${new Date(u.deleted_at).toLocaleDateString()}`}
                  </div>
                </div>
                <div className="user-actions">
                  <Toggle
                    checked={u.status === 'inactive'}
                    onChange={() => handleToggleSuspend(u)}
                  />
                  <button className="btn-delete" onClick={() => setDeleteTarget(u)} aria-label={`Delete ${u.email}`}>
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                      <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2M10 11v6M14 11v6" strokeLinecap="round" strokeLinejoin="round"/>
                    </svg>
                  </button>
                </div>
              </Card>
            ))}
            {users.length === 0 && !error && (
              <div style={{ textAlign: 'center', padding: '40px', color: 'var(--color-text-muted)' }}>
                No users found.
              </div>
            )}
          </div>
        )}
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.UsersTab = UsersTab;
})();
