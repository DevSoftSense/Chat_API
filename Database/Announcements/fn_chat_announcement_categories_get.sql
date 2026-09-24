-- fn_chat_announcement_categories_get
-- Uses SoftOnCloud tab_announcement_category_master.

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_categories_get(
    p_org_id  INTEGER,
    p_app_id  INTEGER,
    p_user_id BIGINT DEFAULT NULL
)
RETURNS TABLE (
    category_id   INTEGER,
    category_name VARCHAR,
    is_active     BOOLEAN,
    announcement_count INTEGER
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    -- Bootstrap master categories when none exist for this tenant (not announcement demo rows).
    IF NOT EXISTS (
        SELECT 1 FROM public.tab_announcement_category_master c
        WHERE c.org_id = p_org_id AND c.app_id = p_app_id
    ) THEN
        INSERT INTO public.tab_announcement_category_master (category_name, org_id, app_id)
        VALUES
            ('HR Department', p_org_id, p_app_id),
            ('IT Department', p_org_id, p_app_id),
            ('Finance Department', p_org_id, p_app_id),
            ('Operations', p_org_id, p_app_id),
            ('General', p_org_id, p_app_id);
    END IF;

    RETURN QUERY
    SELECT
        c.category_id::INTEGER,
        c.category_name::VARCHAR,
        c.is_active,
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcements a
            WHERE a.category_id = c.category_id
              AND a.org_id = p_org_id
              AND a.app_id = p_app_id
              AND COALESCE(a.delete_flag, 0) = 0
              AND LOWER(TRIM(a.status_name)) <> 'deleted'
              AND (
                  p_user_id IS NULL
                  OR a.posted_by = p_user_id
                  OR EXISTS (
                      SELECT 1
                      FROM public.tab_announcement_recipients r
                      WHERE r.announcement_id = a.announcement_id
                        AND r.user_id::BIGINT = p_user_id
                  )
              )
        ) AS announcement_count
    FROM public.tab_announcement_category_master c
    WHERE c.org_id = p_org_id
      AND c.app_id = p_app_id
      AND c.is_active = TRUE
    ORDER BY c.category_name;
END;
$$;
