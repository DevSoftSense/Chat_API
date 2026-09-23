-- Uses existing public.tab_notifications only.
-- Does NOT create/alter tables.
-- No separate read_at column exists; only is_read is updated.

CREATE OR REPLACE FUNCTION public.fn_chat_mark_notification_read(
    p_notification_id bigint,
    p_user_id bigint,
    p_org_id bigint,
    p_app_id integer,
    p_fiscal_year_id integer
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
    fiscal_year_id integer,
    unread_count integer
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
DECLARE
    v_unread integer;
BEGIN
    IF p_notification_id IS NULL OR p_notification_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_mark_notification_read: notification_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_user_id IS NULL OR p_user_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_mark_notification_read: user_id is required'
            USING ERRCODE = '22023';
    END IF;

    UPDATE public.tab_notifications n
    SET is_read = true
    WHERE n.notification_id = p_notification_id
      AND n.user_id = p_user_id
      AND n.org_id = p_org_id
      AND n.app_id = p_app_id
      AND n.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(n.is_deleted, false) = false;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'fn_chat_mark_notification_read: notification not found or not owned by user'
            USING ERRCODE = '42501';
    END IF;

    SELECT COUNT(*)::integer
    INTO v_unread
    FROM public.tab_notifications n
    WHERE n.user_id = p_user_id
      AND n.org_id = p_org_id
      AND n.app_id = p_app_id
      AND n.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(n.is_deleted, false) = false
      AND COALESCE(n.is_read, false) = false;

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
        n.fiscal_year_id,
        COALESCE(v_unread, 0)
    FROM public.tab_notifications n
    WHERE n.notification_id = p_notification_id
      AND n.user_id = p_user_id;
END;
$fn$;

COMMENT ON FUNCTION public.fn_chat_mark_notification_read(bigint, bigint, bigint, integer, integer)
IS 'Marks a notification as read only when it belongs to the authenticated user.';
