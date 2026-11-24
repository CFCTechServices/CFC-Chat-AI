-- Apply only after review against a disposable Supabase project's Auth schema.
-- GoTrue's global logout uses DELETE FROM auth.sessions WHERE user_id = <user>.
-- Its HTTP /logout endpoint needs the target user's JWT, which an admin workflow must not obtain.
-- Verify that refresh tokens cascade from sessions in the target before enabling lifecycle.
BEGIN;

CREATE FUNCTION public.cfc_revoke_banned_user_sessions(target_user_id uuid)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
BEGIN
    -- Serialize against Auth updates to the ban and refuse an unbanned or missing target.
    PERFORM 1 FROM auth.users
    WHERE id = target_user_id AND banned_until > pg_catalog.now()
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'A banned Auth user is required' USING ERRCODE = '22023';
    END IF;
    DELETE FROM auth.sessions WHERE user_id = target_user_id;
END;
$$;

REVOKE ALL ON FUNCTION public.cfc_revoke_banned_user_sessions(uuid) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.cfc_revoke_banned_user_sessions(uuid) TO service_role;

DO $$
BEGIN
    IF pg_catalog.has_function_privilege('anon', 'public.cfc_revoke_banned_user_sessions(uuid)', 'EXECUTE') OR
       pg_catalog.has_function_privilege('authenticated', 'public.cfc_revoke_banned_user_sessions(uuid)', 'EXECUTE') THEN
        RAISE EXCEPTION 'Unexpected inherited execute privilege';
    END IF;
END;
$$;

NOTIFY pgrst, 'reload schema';

COMMIT;
