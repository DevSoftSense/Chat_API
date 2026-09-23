-- Uses existing public.tab_notifications only.
-- Does NOT create/alter tables.

CREATE OR REPLACE FUNCTION public.fn_chat_create_notification(
    p_user_id bigint,
    p_sender_user_id bigint,
    p_title text,
    p_message text,
    p_reference_id bigint,
    p_org_id bigint,
    p_app_id integer,
    p_fiscal_year_id integer,
    p_notification_type character varying DEFAULT 'MESSAGE',
    p_reference_type text DEFAULT 'CHAT',
    p_reference_entity character varying DEFAULT 'tab_messages'
)
RETURNS TABLE (
    notification_id bigint,
    user_id bigint,
    sender_user_id bigint,
    notification_type character varying,
    title text,
    message text,
    reference_id bigint,
    reference_type text,
    reference_entity character varying,
    is_read boolean,
    created_date timestamp without time zone,
    org_id bigint,
    app_id integer,
    fiscal_year_id integer
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
DECLARE
    v_notification_id bigint;
BEGIN
    IF p_user_id IS NULL OR p_user_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_create_notification: user_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_sender_user_id IS NULL OR p_sender_user_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_create_notification: sender_user_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_create_notification: org_id and app_id are required'
            USING ERRCODE = '22023';
    END IF;

    IF p_fiscal_year_id IS NULL OR p_fiscal_year_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_create_notification: fiscal_year_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_reference_id IS NULL OR p_reference_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_create_notification: reference_id (message_id) is required'
            USING ERRCODE = '22023';
    END IF;

    INSERT INTO public.tab_notifications (
        org_id,
        created_date,
        is_deleted,
        is_read,
        message,
        notification_count,
        reference_id,
        reference_type,
        sender_user_id,
        title,
        toast_shown,
        user_id,
        app_id,
        notification_type,
        reference_entity,
        fiscal_year_id
    )
    VALUES (
        p_org_id,
        CURRENT_TIMESTAMP,
        false,
        false,
        COALESCE(NULLIF(btrim(p_message), ''), 'New message'),
        1,
        p_reference_id,
        COALESCE(NULLIF(btrim(p_reference_type), ''), 'CHAT'),
        p_sender_user_id,
        COALESCE(NULLIF(btrim(p_title), ''), 'New Message'),
        false,
        p_user_id,
        p_app_id,
        COALESCE(NULLIF(btrim(p_notification_type), ''), 'MESSAGE'),
        COALESCE(NULLIF(btrim(p_reference_entity), ''), 'tab_messages'),
        p_fiscal_year_id
    )
    RETURNING public.tab_notifications.notification_id INTO v_notification_id;

    RETURN QUERY
    SELECT
        n.notification_id,
        n.user_id,
        n.sender_user_id,
        n.notification_type,
        n.title,
        n.message,
        n.reference_id,
        n.reference_type,
        n.reference_entity,
        n.is_read,
        n.created_date,
        n.org_id,
        n.app_id,
        n.fiscal_year_id
    FROM public.tab_notifications n
    WHERE n.notification_id = v_notification_id;
END;
$fn$;

COMMENT ON FUNCTION public.fn_chat_create_notification(
    bigint, bigint, text, text, bigint, bigint, integer, integer, character varying, text, character varying
) IS 'Creates a MESSAGE notification in tab_notifications after a successful chat message insert.';
