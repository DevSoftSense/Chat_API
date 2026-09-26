-- Synced from live DB. Group path is additive; private path preserved.
-- WARNING: re-applying DROP/CREATE may replace live overloads — review before deploy.

CREATE OR REPLACE FUNCTION public.fn_chat_get_chat_messages(p_chat_id integer, p_user_id bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS TABLE(message_id bigint, chat_id integer, sender_user_id bigint, receiver_user_id bigint, message_body text, message_type_id smallint, type_name character varying, parent_message_id bigint, sent_at timestamp with time zone, read_at timestamp with time zone, created_at timestamp with time zone, updated_at timestamp with time zone, attachment_path_1 text, attachment_path_2 text, attachment_path_3 text, attachment_path_4 text, attachment_path_5 text, is_starred_by_sender boolean, starred_by_sender_at timestamp with time zone, is_starred_by_receiver boolean, starred_by_receiver_at timestamp with time zone, forwarded_from_message_id bigint, forwarded_by bigint, my_reaction_code character varying, my_reaction character varying, peer_reaction_code character varying, peer_reaction character varying, group_id bigint)
 LANGUAGE plpgsql
 STABLE
AS $function$
DECLARE
    v_group_id bigint;
BEGIN
    IF p_chat_id IS NULL
       OR p_user_id IS NULL
       OR p_org_id IS NULL
       OR p_app_id IS NULL
       OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_get_chat_messages: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    SELECT g.group_id
    INTO v_group_id
    FROM public.tab_groups g
    WHERE g.chat_id = p_chat_id
      AND g.org_id = p_org_id
      AND g.app_id = p_app_id
      AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(g.is_deleted, false) = false
    LIMIT 1;

    IF v_group_id IS NOT NULL THEN
        PERFORM public.fn_chat_assert_group_member(
            v_group_id, p_user_id, p_org_id, p_app_id, p_fiscal_year_id);

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
            -- Group read receipts live in tab_message_receipts (not tab_messages.read_at).
            CASE
                WHEN m.sender_user_id IS NOT DISTINCT FROM p_user_id THEN
                    (
                        SELECT MIN(r.read_at)
                        FROM public.tab_message_receipts r
                        WHERE r.message_id = m.message_id
                          AND r.user_id IS DISTINCT FROM p_user_id::integer
                          AND r.read_at IS NOT NULL
                          AND r.org_id = p_org_id
                          AND r.app_id = p_app_id
                          AND r.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
                    )
                ELSE NULL
            END,
            m.created_at,
            m.updated_at,
            m.attachment_path_1::text,
            m.attachment_path_2::text,
            m.attachment_path_3::text,
            m.attachment_path_4::text,
            m.attachment_path_5::text,
            -- Personal star for current user (group: tab_message_stars; fallback sender column).
            CASE
                WHEN m.sender_user_id IS NOT DISTINCT FROM p_user_id THEN
                    COALESCE(
                        (
                            SELECT true
                            FROM public.tab_message_stars s
                            WHERE s.message_id = m.message_id
                              AND s.user_id = p_user_id::integer
                              AND s.org_id = p_org_id
                              AND s.app_id = p_app_id
                              AND s.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
                            LIMIT 1
                        ),
                        m.is_starred_by_sender,
                        false
                    )
                ELSE false
            END,
            CASE
                WHEN m.sender_user_id IS NOT DISTINCT FROM p_user_id THEN
                    COALESCE(
                        (
                            SELECT s.starred_at
                            FROM public.tab_message_stars s
                            WHERE s.message_id = m.message_id
                              AND s.user_id = p_user_id::integer
                              AND s.org_id = p_org_id
                              AND s.app_id = p_app_id
                              AND s.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
                            LIMIT 1
                        ),
                        m.starred_by_sender_at
                    )
                ELSE NULL
            END,
            -- Non-sender members: personal star exposed via receiver columns for API mapping.
            CASE
                WHEN m.sender_user_id IS DISTINCT FROM p_user_id THEN
                    COALESCE(
                        (
                            SELECT true
                            FROM public.tab_message_stars s
                            WHERE s.message_id = m.message_id
                              AND s.user_id = p_user_id::integer
                              AND s.org_id = p_org_id
                              AND s.app_id = p_app_id
                              AND s.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
                            LIMIT 1
                        ),
                        false
                    )
                ELSE COALESCE(m.is_starred_by_receiver, false)
            END,
            CASE
                WHEN m.sender_user_id IS DISTINCT FROM p_user_id THEN
                    (
                        SELECT s.starred_at
                        FROM public.tab_message_stars s
                        WHERE s.message_id = m.message_id
                          AND s.user_id = p_user_id::integer
                          AND s.org_id = p_org_id
                          AND s.app_id = p_app_id
                          AND s.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
                        LIMIT 1
                    )
                ELSE m.starred_by_receiver_at
            END,
            m.forwarded_from_message_id,
            m.forwarded_by,
            my_r.reaction_code,
            my_r.reaction,
            peer_r.reaction_code,
            peer_r.reaction,
            m.group_id
        FROM public.tab_messages m
        LEFT JOIN public.tab_message_type_master t
            ON t.message_type_id = m.message_type_id
           AND t.org_id = m.org_id
           AND t.app_id = m.app_id
           AND t.fiscal_year_id IS NOT DISTINCT FROM m.fiscal_year_id
        LEFT JOIN LATERAL (
            SELECT r.reaction_code, r.reaction
            FROM public.tab_message_reactions r
            WHERE r.message_id = m.message_id
              AND r.user_id = p_user_id
              AND r.app_id = p_app_id
              AND r.org_id = p_org_id
              AND (
                    r.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
                 OR r.fiscal_year_id IS NULL
              )
            ORDER BY r.reacted_on DESC NULLS LAST, r.reaction_id DESC
            LIMIT 1
        ) my_r ON true
        LEFT JOIN LATERAL (
            SELECT
                (
                    SELECT r2.reaction_code
                    FROM public.tab_message_reactions r2
                    WHERE r2.message_id = m.message_id
                      AND r2.user_id IS DISTINCT FROM p_user_id
                      AND r2.app_id = p_app_id
                      AND r2.org_id = p_org_id
                    ORDER BY
                        CASE WHEN r2.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id THEN 0 ELSE 1 END,
                        r2.reacted_on DESC NULLS LAST,
                        r2.reaction_id DESC
                    LIMIT 1
                ) AS reaction_code,
                NULLIF(
                    (
                        SELECT string_agg(DISTINCT r3.reaction, '' ORDER BY r3.reaction)
                        FROM public.tab_message_reactions r3
                        WHERE r3.message_id = m.message_id
                          AND r3.user_id IS DISTINCT FROM p_user_id
                          AND r3.app_id = p_app_id
                          AND r3.org_id = p_org_id
                          AND NULLIF(btrim(COALESCE(r3.reaction, '')), '') IS NOT NULL
                    ),
                    ''
                ) AS reaction
        ) peer_r ON true
        WHERE m.chat_id = p_chat_id
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND m.deleted_at IS NULL
          AND COALESCE(m.is_draft, false) = false
          AND COALESCE(m.delete_flag, 0) = 0
          AND (m.group_id IS NOT DISTINCT FROM v_group_id OR m.group_id IS NULL)
        ORDER BY m.sent_at ASC NULLS LAST, m.message_id ASC;

        RETURN;
    END IF;

    -- Private chat path (existing participant check).
    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_messages m
        WHERE m.chat_id = p_chat_id
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND (
                m.sender_user_id = p_user_id
             OR m.receiver_user_id = p_user_id
          )
    ) THEN
        RETURN;
    END IF;

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
        COALESCE(m.is_starred_by_sender, false),
        m.starred_by_sender_at,
        COALESCE(m.is_starred_by_receiver, false),
        m.starred_by_receiver_at,
        m.forwarded_from_message_id,
        m.forwarded_by,
        my_r.reaction_code,
        my_r.reaction,
        peer_r.reaction_code,
        peer_r.reaction,
        m.group_id
    FROM public.tab_messages m
    LEFT JOIN public.tab_message_type_master t
        ON t.message_type_id = m.message_type_id
       AND t.org_id = m.org_id
       AND t.app_id = m.app_id
       AND t.fiscal_year_id IS NOT DISTINCT FROM m.fiscal_year_id
    LEFT JOIN LATERAL (
        SELECT r.reaction_code, r.reaction
        FROM public.tab_message_reactions r
        WHERE r.message_id = m.message_id
          AND r.user_id = p_user_id
          AND r.app_id = p_app_id
          AND r.org_id = p_org_id
          AND r.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
        ORDER BY r.reacted_on DESC NULLS LAST, r.reaction_id DESC
        LIMIT 1
    ) my_r ON true
    LEFT JOIN LATERAL (
        SELECT r.reaction_code, r.reaction
        FROM public.tab_message_reactions r
        WHERE r.message_id = m.message_id
          AND r.user_id IS DISTINCT FROM p_user_id
          AND r.app_id = p_app_id
          AND r.org_id = p_org_id
          AND r.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
        ORDER BY r.reacted_on DESC NULLS LAST, r.reaction_id DESC
        LIMIT 1
    ) peer_r ON true
    WHERE m.chat_id = p_chat_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.group_id IS NULL
      AND (
            m.sender_user_id = p_user_id
         OR m.receiver_user_id = p_user_id
      )
    ORDER BY m.sent_at ASC NULLS LAST, m.message_id ASC;
END;
$function$

