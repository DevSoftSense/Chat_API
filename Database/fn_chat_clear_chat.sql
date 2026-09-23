-- Soft-clear all messages in a chat via delete_flag (no physical DELETE).
-- Sets delete_flag = 1 for every visible message in the chat.

DROP FUNCTION IF EXISTS public.fn_chat_clear_chat(integer, bigint, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_clear_chat(
    p_chat_id integer,
    p_user_id bigint,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer
)
RETURNS TABLE (
    chat_id integer,
    affected_count integer,
    peer_user_id bigint,
    cleared_by bigint,
    cleared_at timestamp with time zone
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
DECLARE
    v_now timestamptz := CURRENT_TIMESTAMP;
    v_peer bigint;
    v_count integer := 0;
BEGIN
    IF p_chat_id IS NULL
       OR p_user_id IS NULL
       OR p_org_id IS NULL
       OR p_app_id IS NULL
       OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_clear_chat: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    IF p_chat_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_clear_chat: chat_id is required'
            USING ERRCODE = '22023';
    END IF;

    -- Access: user must appear as sender or receiver on any message in this chat (incl. already-deleted).
    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_messages m
        WHERE m.chat_id = p_chat_id
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND COALESCE(m.is_draft, false) = false
          AND (
                m.sender_user_id = p_user_id
             OR m.receiver_user_id = p_user_id
          )
    ) THEN
        RAISE EXCEPTION 'fn_chat_clear_chat: chat % not found or user % is not a participant',
            p_chat_id, p_user_id
            USING ERRCODE = '42501';
    END IF;

    SELECT CASE
               WHEN m.sender_user_id = p_user_id THEN m.receiver_user_id
               ELSE m.sender_user_id
           END
    INTO v_peer
    FROM public.tab_messages m
    WHERE m.chat_id = p_chat_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(m.is_draft, false) = false
      AND (
            m.sender_user_id = p_user_id
         OR m.receiver_user_id = p_user_id
      )
    ORDER BY m.message_id DESC
    LIMIT 1;

    UPDATE public.tab_messages m
    SET delete_flag = 1::smallint,
        deleted_at = v_now,
        updated_at = v_now
    WHERE m.chat_id = p_chat_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(m.is_draft, false) = false
      AND COALESCE(m.delete_flag, 0) = 0;

    GET DIAGNOSTICS v_count = ROW_COUNT;

    chat_id := p_chat_id;
    affected_count := v_count;
    peer_user_id := v_peer;
    cleared_by := p_user_id;
    cleared_at := v_now;
    RETURN NEXT;
END;
$fn$;

COMMENT ON FUNCTION public.fn_chat_clear_chat(integer, bigint, integer, integer, integer)
IS 'Soft-clears all messages in a chat (delete_flag=1). Never physically deletes rows.';
