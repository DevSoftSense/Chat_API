-- Uses existing public.tab_messages only. Does NOT create/alter tables.
-- Empty private chats are anchored with a draft placeholder row (is_draft=true),
-- which fn_chat_get_chat_messages excludes.

CREATE OR REPLACE FUNCTION public.fn_chat_get_or_create_private_chat(
    p_user1_id bigint,
    p_user2_id bigint,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer
)
RETURNS TABLE (
    chat_id integer
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
DECLARE
    v_low bigint;
    v_high bigint;
    v_chat_id integer;
    v_type_id smallint;
BEGIN
    IF p_user1_id IS NULL
       OR p_user2_id IS NULL
       OR p_org_id IS NULL
       OR p_app_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_get_or_create_private_chat: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    IF p_user1_id = p_user2_id THEN
        RAISE EXCEPTION 'fn_chat_get_or_create_private_chat: user1 and user2 must be different'
            USING ERRCODE = '22023';
    END IF;

    v_low := LEAST(p_user1_id, p_user2_id);
    v_high := GREATEST(p_user1_id, p_user2_id);

    PERFORM pg_advisory_xact_lock(
        hashtext(
            p_org_id::text || ':' ||
            p_app_id::text || ':' ||
            COALESCE(p_fiscal_year_id::text, 'null') || ':' ||
            v_low::text || ':' ||
            v_high::text
        )
    );

    SELECT m.chat_id
    INTO v_chat_id
    FROM public.tab_messages m
    WHERE m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND m.deleted_at IS NULL
      AND (
            (m.sender_user_id = v_low AND m.receiver_user_id = v_high)
         OR (m.sender_user_id = v_high AND m.receiver_user_id = v_low)
          )
    ORDER BY m.message_id ASC
    LIMIT 1;

    IF v_chat_id IS NOT NULL THEN
        chat_id := v_chat_id;
        RETURN NEXT;
        RETURN;
    END IF;

    -- Same id space as groups — avoid colliding with tab_groups.chat_id.
    SELECT COALESCE(MAX(m.chat_id), 0) + 1
    INTO v_chat_id
    FROM public.tab_messages m;

    SELECT GREATEST(v_chat_id, COALESCE(MAX(g.chat_id), 0) + 1)
    INTO v_chat_id
    FROM public.tab_groups g;

    SELECT t.message_type_id::smallint
    INTO v_type_id
    FROM public.tab_message_type_master t
    WHERE t.org_id = p_org_id
      AND t.app_id = p_app_id
      AND t.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(t.is_active, true) = true
    ORDER BY t.message_type_id ASC
    LIMIT 1;

    IF v_type_id IS NULL THEN
        v_type_id := 1;
    END IF;

    INSERT INTO public.tab_messages (
        chat_id,
        sender_user_id,
        receiver_user_id,
        message_body,
        message_type_id,
        is_draft,
        scheduled_at,
        sent_at,
        read_at,
        created_at,
        updated_at,
        org_id,
        app_id,
        fiscal_year_id,
        is_starred_by_sender,
        is_starred_by_receiver
    )
    VALUES (
        v_chat_id,
        v_low,
        v_high,
        '',
        v_type_id,
        true,
        NULL,
        NULL,
        CURRENT_TIMESTAMP,
        CURRENT_TIMESTAMP,
        CURRENT_TIMESTAMP,
        p_org_id,
        p_app_id,
        p_fiscal_year_id,
        false,
        false
    );

    chat_id := v_chat_id;
    RETURN NEXT;
END;
$fn$;

COMMENT ON FUNCTION public.fn_chat_get_or_create_private_chat(bigint, bigint, integer, integer, integer)
IS 'Gets or creates a 1-to-1 private chat_id for two SoftOnCloud user ids using tab_messages only.';
