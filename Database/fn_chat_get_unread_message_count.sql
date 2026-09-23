-- Global unread MESSAGE count for the authenticated receiver.
-- Uses existing public.tab_messages only (no new table).
-- Counts messages, not chats.

CREATE OR REPLACE FUNCTION public.fn_chat_get_unread_message_count(
    p_user_id bigint,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer
)
RETURNS integer
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
DECLARE
    v_count integer;
BEGIN
    IF p_user_id IS NULL OR p_user_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_get_unread_message_count: user_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_org_id IS NULL OR p_org_id <= 0
       OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_get_unread_message_count: org_id and app_id are required'
            USING ERRCODE = '22023';
    END IF;

    SELECT COUNT(*)::integer
    INTO v_count
    FROM public.tab_messages m
    WHERE m.receiver_user_id = p_user_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND m.read_at IS NULL
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL
      AND m.sender_user_id IS DISTINCT FROM p_user_id;

    RETURN COALESCE(v_count, 0);
END;
$fn$;

COMMENT ON FUNCTION public.fn_chat_get_unread_message_count(bigint, integer, integer, integer)
IS 'Returns count of unread inbound messages for a user (receiver, read_at IS NULL, delete_flag = 0).';

-- Per-peer unread MESSAGE counts for the authenticated receiver (WhatsApp-style chat list badges).
CREATE OR REPLACE FUNCTION public.fn_chat_get_unread_counts_by_peer(
    p_user_id bigint,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer
)
RETURNS TABLE (
    peer_user_id bigint,
    unread_count integer
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
BEGIN
    IF p_user_id IS NULL OR p_user_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_get_unread_counts_by_peer: user_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_org_id IS NULL OR p_org_id <= 0
       OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_get_unread_counts_by_peer: org_id and app_id are required'
            USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT
        m.sender_user_id AS peer_user_id,
        COUNT(*)::integer AS unread_count
    FROM public.tab_messages m
    WHERE m.receiver_user_id = p_user_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND m.read_at IS NULL
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL
      AND m.sender_user_id IS DISTINCT FROM p_user_id
    GROUP BY m.sender_user_id
    HAVING COUNT(*) > 0
    ORDER BY m.sender_user_id;
END;
$fn$;

COMMENT ON FUNCTION public.fn_chat_get_unread_counts_by_peer(bigint, integer, integer, integer)
IS 'Returns unread inbound message counts grouped by sender (peer) for chat-list badges.';
