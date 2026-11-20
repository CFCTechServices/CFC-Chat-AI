-- Read-only catalog evidence. Run on the intended disposable project before changing ACLs or lifecycle settings.
SELECT conrelid::regclass AS source, confrelid::regclass AS target, conname, pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE contype = 'f' AND confrelid IN ('auth.users'::regclass, 'auth.sessions'::regclass,
    'public.profiles'::regclass, 'public.chat_sessions'::regclass, 'public.chat_messages'::regclass)
ORDER BY 1, 2, 3;

SELECT relrowsecurity, relforcerowsecurity, relacl FROM pg_class WHERE oid = 'public.profiles'::regclass;
SELECT attname, attacl FROM pg_attribute WHERE attrelid = 'public.profiles'::regclass AND attnum > 0 AND NOT attisdropped;
SELECT r.rolname AS grantee, parent.rolname AS member_of
FROM pg_roles r CROSS JOIN pg_roles parent
WHERE r.rolname IN ('anon', 'authenticated', 'service_role') AND r.oid <> parent.oid
    AND pg_has_role(r.oid, parent.oid, 'USAGE');
SELECT rolname, attname,
       has_column_privilege(rolname, 'public.profiles', attname, 'SELECT') AS can_select,
       has_column_privilege(rolname, 'public.profiles', attname, 'INSERT') AS can_insert,
       has_column_privilege(rolname, 'public.profiles', attname, 'UPDATE') AS can_update
FROM pg_roles CROSS JOIN pg_attribute
WHERE rolname IN ('anon', 'authenticated', 'service_role') AND attrelid = 'public.profiles'::regclass
    AND attnum > 0 AND NOT attisdropped
ORDER BY rolname, attnum;
SELECT * FROM pg_policies WHERE schemaname = 'public' AND tablename = 'profiles';
SELECT tgrelid::regclass AS relation, pg_get_triggerdef(t.oid) AS trigger_definition,
       pg_get_userbyid(p.proowner) AS function_owner, p.prosecdef AS security_definer, p.proconfig AS function_settings,
       pg_get_functiondef(p.oid) AS function_definition
FROM pg_trigger t JOIN pg_proc p ON p.oid = t.tgfoid
WHERE NOT tgisinternal AND tgrelid IN ('auth.users'::regclass, 'public.profiles'::regclass);
