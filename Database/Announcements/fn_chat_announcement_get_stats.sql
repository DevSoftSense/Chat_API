-- fn_chat_announcement_get_stats + fn_chat_announcement_unread_count

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_get_stats(
    p_user_id BIGINT,
    p_org_id  INTEGER,
    p_app_id  INTEGER
)
RETURNS TABLE (
    total_announcements INTEGER,
    total_reads         INTEGER,
    unread_by_me        INTEGER
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcements a
            WHERE a.org_id = p_org_id
              AND a.app_id = p_app_id
              AND COALESCE(a.delete_flag, 0) = 0
              AND LOWER(TRIM(a.status_name)) <> 'deleted'
              AND (
                  a.posted_by = p_user_id
                  OR EXISTS (
                      SELECT 1 FROM public.tab_announcement_recipients r
                      WHERE r.announcement_id = a.announcement_id
                        AND r.user_id::BIGINT = p_user_id
                  )
              )
        ) AS total_announcements,
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcement_recipients r
            INNER JOIN public.tab_announcements a ON a.announcement_id = r.announcement_id
            WHERE a.org_id = p_org_id
              AND a.app_id = p_app_id
              AND COALESCE(a.delete_flag, 0) = 0
              AND LOWER(TRIM(a.status_name)) <> 'deleted'
              AND LOWER(TRIM(r.read_status)) = 'read'
              AND (
                  a.posted_by = p_user_id
                  OR EXISTS (
                      SELECT 1 FROM public.tab_announcement_recipients r2
                      WHERE r2.announcement_id = a.announcement_id
                        AND r2.user_id::BIGINT = p_user_id
                  )
              )
        ) AS total_reads,
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcement_recipients r
            INNER JOIN public.tab_announcements a ON a.announcement_id = r.announcement_id
            WHERE r.user_id::BIGINT = p_user_id
              AND LOWER(TRIM(r.read_status)) <> 'read'
              AND a.org_id = p_org_id
              AND a.app_id = p_app_id
              AND COALESCE(a.delete_flag, 0) = 0
              AND LOWER(TRIM(a.status_name)) <> 'deleted'
        ) AS unread_by_me;
END;
$$;

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_unread_count(
    p_user_id BIGINT,
    p_org_id  INTEGER,
    p_app_id  INTEGER
)
RETURNS TABLE (unread_count INTEGER)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT COALESCE(s.unread_by_me, 0)::INTEGER AS unread_count
    FROM public.fn_chat_announcement_get_stats(p_user_id, p_org_id, p_app_id) s;
END;
$$;
