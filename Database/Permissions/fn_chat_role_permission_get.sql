-- fn_chat_role_permission_get
-- Reads role × tab flags from public.tab_role_permission
-- joined to public.tab_menu_master (tab_id = menu_id).
-- Used by Chat SoftOnCloud Manage Permissions + dynamic sidebar menu.
--
-- Pass either p_role_id (>0) OR p_role_code (non-empty). Prefer role_id.

DROP FUNCTION IF EXISTS public.fn_chat_role_permission_get(INTEGER, INTEGER, INTEGER, VARCHAR);
DROP FUNCTION IF EXISTS public.fn_chat_role_permission_roles(INTEGER, INTEGER);

CREATE OR REPLACE FUNCTION public.fn_chat_role_permission_roles(
    p_org_id INTEGER,
    p_app_id INTEGER
)
RETURNS TABLE (
    role_id   INTEGER,
    role_code VARCHAR
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT DISTINCT
        p.role_id,
        COALESCE(p.role_code, '')::VARCHAR AS role_code
    FROM public.tab_role_permission p
    WHERE p.org_id = p_org_id
      AND p.app_id = p_app_id
    ORDER BY p.role_id;
END;
$$;

CREATE OR REPLACE FUNCTION public.fn_chat_role_permission_get(
    p_org_id            INTEGER,
    p_app_id            INTEGER,
    p_role_id           INTEGER DEFAULT NULL,
    p_role_code         VARCHAR DEFAULT NULL
)
RETURNS TABLE (
    permission_id   INTEGER,
    menu_id         INTEGER,
    role_id         INTEGER,
    role_code       VARCHAR,
    can_view        BOOLEAN,
    can_add         BOOLEAN,
    can_edit        BOOLEAN,
    can_delete      BOOLEAN,
    is_active       BOOLEAN,
    menu_code       VARCHAR,
    menu_name       VARCHAR,
    menu_url        VARCHAR,
    icon_name       VARCHAR,
    parent_menu_id  INTEGER,
    sort_order      INTEGER
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_use_role_id   BOOLEAN;
    v_role_id       INTEGER;
    v_role_code     VARCHAR;
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    v_use_role_id := (p_role_id IS NOT NULL AND p_role_id > 0);
    v_role_id     := COALESCE(p_role_id, 0);
    v_role_code   := COALESCE(NULLIF(TRIM(p_role_code), ''), '');

    IF NOT v_use_role_id AND v_role_code = '' THEN
        RAISE EXCEPTION 'roleId or roleCode is required' USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT
        COALESCE(p.permission_id, 0)::INTEGER AS permission_id,
        m.menu_id,
        COALESCE(p.role_id, v_role_id)::INTEGER AS role_id,
        COALESCE(NULLIF(TRIM(p.role_code), ''), v_role_code)::VARCHAR AS role_code,
        COALESCE(p.can_view, false)   AS can_view,
        COALESCE(p.can_add, false)    AS can_add,
        COALESCE(p.can_edit, false)   AS can_edit,
        COALESCE(p.can_delete, false) AS can_delete,
        COALESCE(p.is_active, false)  AS is_active,
        m.menu_code,
        m.menu_name,
        m.menu_url,
        m.icon_name,
        m.parent_menu_id,
        m.sort_order
    FROM public.tab_menu_master m
    LEFT JOIN public.tab_role_permission p
           ON p.tab_id = m.menu_id
          AND p.org_id = p_org_id
          AND p.app_id = p_app_id
          AND (
                (v_use_role_id AND p.role_id = v_role_id)
             OR ((NOT v_use_role_id) AND UPPER(TRIM(p.role_code)) = UPPER(TRIM(v_role_code)))
          )
    WHERE m.app_id = p_app_id
      AND COALESCE(m.is_deleted, false) = false
      AND COALESCE(m.is_active, true) = true
    ORDER BY m.sort_order, m.menu_id;
END;
$$;

COMMENT ON FUNCTION public.fn_chat_role_permission_get(INTEGER, INTEGER, INTEGER, VARCHAR) IS
    'Chat SoftOnCloud: menu + role permission flags (tab_menu_master JOIN tab_role_permission).';

COMMENT ON FUNCTION public.fn_chat_role_permission_roles(INTEGER, INTEGER) IS
    'Chat SoftOnCloud: distinct roles from tab_role_permission for an org/app.';

-- Examples:
-- SELECT * FROM public.fn_chat_role_permission_roles(100004, 5);
-- SELECT * FROM public.fn_chat_role_permission_get(100004, 5, 15, NULL);
-- SELECT * FROM public.fn_chat_role_permission_get(100004, 5, NULL, 'CHATS_EMPLOYEE');
