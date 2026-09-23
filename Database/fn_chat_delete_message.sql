-- Soft-delete for everyone via delete_flag (no physical DELETE).
-- Uses existing public.tab_messages only.

DROP FUNCTION IF EXISTS public.fn_chat_delete_message(bigint, bigint, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_delete_message(
    p_message_id bigint,
    p_user_id bigint,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer
)
RETURNS TABLE (
    message_id bigint,
    chat_id integer,
    sender_user_id bigint,
    receiver_user_id bigint,
    deleted_by bigint,
    deleted_at timestamp with time zone,
    delete_flag smallint
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
DECLARE
    v_msg public.tab_messages%ROWTYPE;
    v_deleted_at timestamptz;
BEGIN
    IF p_message_id IS NULL
       OR p_user_id IS NULL
       OR p_org_id IS NULL
       OR p_app_id IS NULL
       OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_delete_message: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    SELECT *
    INTO v_msg
    FROM public.tab_messages m
    WHERE m.message_id = p_message_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(m.is_draft, false) = false;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'fn_chat_delete_message: message % not found', p_message_id
            USING ERRCODE = 'P0002';
    END IF;

    IF COALESCE(v_msg.delete_flag, 0) <> 0 OR v_msg.deleted_at IS NOT NULL THEN
        RAISE EXCEPTION 'fn_chat_delete_message: message % is already deleted', p_message_id
            USING ERRCODE = '22023';
    END IF;

    IF v_msg.sender_user_id IS DISTINCT FROM p_user_id
       AND v_msg.receiver_user_id IS DISTINCT FROM p_user_id THEN
        RAISE EXCEPTION 'fn_chat_delete_message: user % is not allowed to delete message %',
            p_user_id, p_message_id
            USING ERRCODE = '42501';
    END IF;

    v_deleted_at := CURRENT_TIMESTAMP;

    UPDATE public.tab_messages m
    SET delete_flag = 1::smallint,
        deleted_at = v_deleted_at,
        updated_at = v_deleted_at
    WHERE m.message_id = p_message_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'fn_chat_delete_message: message % could not be deleted', p_message_id
            USING ERRCODE = 'P0001';
    END IF;

    message_id := v_msg.message_id;
    chat_id := v_msg.chat_id;
    sender_user_id := v_msg.sender_user_id;
    receiver_user_id := v_msg.receiver_user_id;
    deleted_by := p_user_id;
    deleted_at := v_deleted_at;
    delete_flag := 1::smallint;
    RETURN NEXT;
END;
$fn$;

COMMENT ON FUNCTION public.fn_chat_delete_message(bigint, bigint, integer, integer, integer)
IS 'Soft-deletes a chat message for everyone (delete_flag=1 + deleted_at). Never physically deletes the row.';
