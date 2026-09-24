-- fn_chat_announcement_delete
-- Soft-delete ONLY: delete_flag = 1, delete_at = now()
-- NEVER physically deletes the row / recipients.

DROP FUNCTION IF EXISTS public.fn_chat_announcement_delete(BIGINT, BIGINT, INTEGER, INTEGER);

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_delete(
    p_announcement_id BIGINT,
    p_user_id         BIGINT,
    p_org_id          INTEGER,
    p_app_id          INTEGER
)
RETURNS TABLE (
    result_announcement_id BIGINT,
    result_deleted         BOOLEAN
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_posted_by BIGINT;
    v_delete_at TIMESTAMPTZ;
BEGIN
    SELECT a.posted_by INTO v_posted_by
    FROM public.tab_announcements a
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
      AND COALESCE(a.delete_flag, 0) = 0;

    IF v_posted_by IS NULL THEN
        RAISE EXCEPTION 'Announcement not found' USING ERRCODE = 'P0002';
    END IF;
    IF v_posted_by <> p_user_id THEN
        RAISE EXCEPTION 'Only the poster can delete this announcement' USING ERRCODE = '42501';
    END IF;

    v_delete_at := CURRENT_TIMESTAMP;

    UPDATE public.tab_announcements a
    SET delete_flag = 1::SMALLINT,
        delete_at   = v_delete_at,
        updated_at  = v_delete_at
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
      AND COALESCE(a.delete_flag, 0) = 0;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Announcement not found' USING ERRCODE = 'P0002';
    END IF;

    result_announcement_id := p_announcement_id;
    result_deleted := TRUE;
    RETURN NEXT;
END;
$$;

COMMENT ON FUNCTION public.fn_chat_announcement_delete(BIGINT, BIGINT, INTEGER, INTEGER)
IS 'Soft-deletes an announcement (delete_flag=1 + delete_at). Never physically deletes the row.';
