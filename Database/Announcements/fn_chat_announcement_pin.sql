-- fn_chat_announcement_pin / unpin
-- OUT columns renamed to avoid plpgsql "announcement_id is ambiguous".
-- Must DROP first: CREATE OR REPLACE cannot change RETURNS TABLE column names.

DROP FUNCTION IF EXISTS public.fn_chat_announcement_unpin(BIGINT, BIGINT, INTEGER, INTEGER);
DROP FUNCTION IF EXISTS public.fn_chat_announcement_pin(BIGINT, BIGINT, INTEGER, INTEGER, BOOLEAN);
DROP FUNCTION IF EXISTS public.fn_chat_announcement_pin(BIGINT, BIGINT, INTEGER, INTEGER);

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_pin(
    p_announcement_id BIGINT,
    p_user_id         BIGINT,
    p_org_id          INTEGER,
    p_app_id          INTEGER,
    p_pinned          BOOLEAN DEFAULT TRUE
)
RETURNS TABLE (
    result_announcement_id BIGINT,
    result_is_pinned       BOOLEAN
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_posted_by BIGINT;
    v_is_pinned BOOLEAN;
BEGIN
    SELECT a.posted_by
    INTO v_posted_by
    FROM public.tab_announcements a
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
      AND COALESCE(a.delete_flag, 0) = 0
      AND LOWER(TRIM(a.status_name)) <> 'deleted';

    IF v_posted_by IS NULL THEN
        RAISE EXCEPTION 'Announcement not found' USING ERRCODE = 'P0002';
    END IF;
    IF v_posted_by <> p_user_id THEN
        RAISE EXCEPTION 'Only the poster can pin/unpin this announcement' USING ERRCODE = '42501';
    END IF;

    UPDATE public.tab_announcements a
    SET is_pinned = COALESCE(p_pinned, TRUE),
        updated_at = NOW()
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
    RETURNING a.is_pinned INTO v_is_pinned;

    result_announcement_id := p_announcement_id;
    result_is_pinned := COALESCE(v_is_pinned, FALSE);
    RETURN NEXT;
END;
$$;

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_unpin(
    p_announcement_id BIGINT,
    p_user_id         BIGINT,
    p_org_id          INTEGER,
    p_app_id          INTEGER
)
RETURNS TABLE (
    result_announcement_id BIGINT,
    result_is_pinned       BOOLEAN
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT r.result_announcement_id, r.result_is_pinned
    FROM public.fn_chat_announcement_pin(
        p_announcement_id, p_user_id, p_org_id, p_app_id, FALSE
    ) AS r;
END;
$$;
