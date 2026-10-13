-- fn_chat_menu_master_get
-- Reads sidebar/menu rows from public.tab_menu_master (per app/product).
-- Used by Chat SoftOnCloud dynamic menu / Manage Permissions.

DROP FUNCTION IF EXISTS public.fn_chat_menu_master_get(INTEGER);

CREATE OR REPLACE FUNCTION public.fn_chat_menu_master_get(
    p_app_id INTEGER
)
RETURNS TABLE (
    menu_id         INTEGER,
    app_id          INTEGER,
    parent_menu_id  INTEGER,
    menu_code       VARCHAR,
    menu_name       VARCHAR,
    menu_url        VARCHAR,
    icon_name       VARCHAR,
    sort_order      INTEGER,
    is_active       BOOLEAN,
    is_deleted      BOOLEAN
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'appId is required' USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT
        m.menu_id,
        m.app_id,
        m.parent_menu_id,
        m.menu_code,
        m.menu_name,
        m.menu_url,
        m.icon_name,
        m.sort_order,
        COALESCE(m.is_active, true)   AS is_active,
        COALESCE(m.is_deleted, false) AS is_deleted
    FROM public.tab_menu_master m
    WHERE m.app_id = p_app_id
      AND COALESCE(m.is_deleted, false) = false
      AND COALESCE(m.is_active, true) = true
    ORDER BY m.sort_order, m.menu_id;
END;
$$;

COMMENT ON FUNCTION public.fn_chat_menu_master_get(INTEGER) IS
    'Chat SoftOnCloud: active menus from tab_menu_master for an app_id.';

-- Example:
-- SELECT * FROM public.fn_chat_menu_master_get(5);
