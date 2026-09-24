-- fn_chat_announcement_get_by_id
-- DROP required when RETURNS TABLE gains recipient_user_ids.

DROP FUNCTION IF EXISTS public.fn_chat_announcement_get_by_id(BIGINT, BIGINT, INTEGER, INTEGER);

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_get_by_id(
    p_announcement_id BIGINT,
    p_user_id         BIGINT,
    p_org_id          INTEGER,
    p_app_id          INTEGER
)
RETURNS TABLE (
    announcement_id       BIGINT,
    title                 VARCHAR,
    description           TEXT,
    category_id           INTEGER,
    category_name         VARCHAR,
    department_id         INTEGER,
    department_name       VARCHAR,
    location              VARCHAR,
    is_important          BOOLEAN,
    is_pinned             BOOLEAN,
    status_id             INTEGER,
    status_name           VARCHAR,
    posted_by             BIGINT,
    posted_by_name        VARCHAR,
    posted_date           TIMESTAMPTZ,
    expiry_date           TIMESTAMPTZ,
    read_count            INTEGER,
    recipient_count       INTEGER,
    is_read_by_current_user BOOLEAN,
    recipient_user_ids    BIGINT[]
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_announcement_id IS NULL OR p_announcement_id <= 0 THEN
        RAISE EXCEPTION 'announcementId is required' USING ERRCODE = '22023';
    END IF;
    IF p_user_id IS NULL OR p_user_id <= 0 THEN
        RAISE EXCEPTION 'userId is required' USING ERRCODE = '22023';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_announcements a
        WHERE a.announcement_id = p_announcement_id
          AND a.org_id = p_org_id
          AND a.app_id = p_app_id
          AND COALESCE(a.delete_flag, 0) = 0
          AND LOWER(TRIM(a.status_name)) <> 'deleted'
          AND (
              a.posted_by = p_user_id
              OR EXISTS (
                  SELECT 1
                  FROM public.tab_announcement_recipients r
                  WHERE r.announcement_id = a.announcement_id
                    AND r.user_id::BIGINT = p_user_id
              )
          )
    ) THEN
        RAISE EXCEPTION 'Announcement not found' USING ERRCODE = 'P0002';
    END IF;

    RETURN QUERY
    SELECT
        a.announcement_id,
        a.title::VARCHAR,
        a.description,
        a.category_id::INTEGER,
        cat.category_name::VARCHAR,
        a.department_id::INTEGER,
        dept.department_name::VARCHAR,
        a.location::VARCHAR,
        COALESCE(a.is_important, LOWER(TRIM(a.status_name)) = 'important') AS is_important,
        a.is_pinned,
        CASE
            WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 3
            WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 2
            WHEN LOWER(TRIM(a.status_name)) IN ('expired') THEN 3
            WHEN LOWER(TRIM(a.status_name)) IN ('expiring soon') THEN 2
            ELSE 1
        END AS status_id,
        CASE
            WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 'Expired'::VARCHAR
            WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 'Expiring Soon'::VARCHAR
            WHEN LOWER(TRIM(a.status_name)) = 'important' THEN 'Active'::VARCHAR
            ELSE a.status_name::VARCHAR
        END AS status_name,
        a.posted_by,
        NULL::VARCHAR AS posted_by_name,
        COALESCE(a.published_at, a.created_at) AS posted_date,
        a.expires_at AS expiry_date,
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcement_recipients r
            WHERE r.announcement_id = a.announcement_id
              AND LOWER(TRIM(r.read_status)) = 'read'
        ) AS read_count,
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcement_recipients r
            WHERE r.announcement_id = a.announcement_id
        ) AS recipient_count,
        COALESCE((
            SELECT LOWER(TRIM(r.read_status)) = 'read'
            FROM public.tab_announcement_recipients r
            WHERE r.announcement_id = a.announcement_id
              AND r.user_id::BIGINT = p_user_id
            LIMIT 1
        ), FALSE) AS is_read_by_current_user,
        COALESCE((
            SELECT array_agg(r.user_id::BIGINT ORDER BY r.user_id)
            FROM public.tab_announcement_recipients r
            WHERE r.announcement_id = a.announcement_id
        ), ARRAY[]::BIGINT[]) AS recipient_user_ids
    FROM public.tab_announcements a
    LEFT JOIN public.tab_announcement_category_master cat
        ON cat.category_id = a.category_id
    LEFT JOIN public.tab_department_master dept
        ON dept.department_id = a.department_id::INTEGER
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
      AND COALESCE(a.delete_flag, 0) = 0;
END;
$$;
