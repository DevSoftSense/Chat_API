-- Soft FY: do not filter by fiscal_year_id at all (org+app+user only).

DROP FUNCTION IF EXISTS public.fn_chat_get_starred_messages(bigint, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_get_starred_messages(
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
    message_body text,
    message_type_id smallint,
    type_name character varying,
    parent_message_id bigint,
    sent_at timestamp with time zone,
    read_at timestamp with time zone,
    created_at timestamp with time zone,
    updated_at timestamp with time zone,
    attachment_path_1 text,
    attachment_path_2 text,
    attachment_path_3 text,
    attachment_path_4 text,
    attachment_path_5 text,
    is_starred_by_sender boolean,
    starred_by_sender_at timestamp with time zone,
    is_starred_by_receiver boolean,
    starred_by_receiver_at timestamp with time zone,
    is_starred_by_me boolean,
    starred_at timestamp with time zone,
    forwarded_from_message_id bigint,
    forwarded_by bigint,
    peer_user_id bigint
)
LANGUAGE plpgsql
STABLE
SECURITY INVOKER
AS $fn$
BEGIN
    IF p_user_id IS NULL OR p_org_id IS NULL OR p_app_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_get_starred_messages: user_id, org_id and app_id are required'
            USING ERRCODE = '22023';
    END IF;

    -- p_fiscal_year_id kept for API signature compatibility; not applied as filter.
    PERFORM p_fiscal_year_id;

    RETURN QUERY
    SELECT
        m.message_id,
        m.chat_id,
        m.sender_user_id,
        m.receiver_user_id,
        m.message_body,
        m.message_type_id,
        t.type_name,
        m.parent_message_id,
        m.sent_at,
        m.read_at,
        m.created_at,
        m.updated_at,
        m.attachment_path_1::text,
        m.attachment_path_2::text,
        m.attachment_path_3::text,
        m.attachment_path_4::text,
        m.attachment_path_5::text,
        m.is_starred_by_sender,
        m.starred_by_sender_at,
        m.is_starred_by_receiver,
        m.starred_by_receiver_at,
        true AS is_starred_by_me,
        CASE
            WHEN m.sender_user_id = p_user_id THEN m.starred_by_sender_at
            ELSE m.starred_by_receiver_at
        END AS starred_at,
        m.forwarded_from_message_id,
        m.forwarded_by,
        CASE
            WHEN m.sender_user_id = p_user_id THEN m.receiver_user_id
            ELSE m.sender_user_id
        END AS peer_user_id
    FROM public.tab_messages m
    LEFT JOIN public.tab_message_type_master t
        ON t.message_type_id = m.message_type_id
       AND t.org_id = m.org_id
       AND t.app_id = m.app_id
    WHERE m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.deleted_at IS NULL
      AND COALESCE(m.delete_flag, 0) = 0
      AND COALESCE(m.is_draft, false) = false
      AND (
            (m.sender_user_id = p_user_id AND COALESCE(m.is_starred_by_sender, false) = true)
         OR (m.receiver_user_id = p_user_id AND COALESCE(m.is_starred_by_receiver, false) = true)
      )
    ORDER BY
        CASE
            WHEN m.sender_user_id = p_user_id THEN m.starred_by_sender_at
            ELSE m.starred_by_receiver_at
        END DESC NULLS LAST,
        m.message_id DESC;
END;
$fn$;
