-- Synced from live DB. Group path is additive; private path preserved.
-- WARNING: re-applying DROP/CREATE may replace live overloads — review before deploy.

CREATE OR REPLACE FUNCTION public.fn_chat_send_message(p_chat_id integer, p_sender_user_id bigint, p_receiver_user_id bigint, p_message_body text, p_message_type_id smallint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer, p_attachment_path_1 text DEFAULT NULL::text, p_attachment_path_2 text DEFAULT NULL::text, p_attachment_path_3 text DEFAULT NULL::text, p_attachment_path_4 text DEFAULT NULL::text, p_attachment_path_5 text DEFAULT NULL::text, p_group_id bigint DEFAULT NULL::bigint)
 RETURNS TABLE(message_id bigint, chat_id integer, sender_user_id bigint, receiver_user_id bigint, message_body text, message_type_id smallint, sent_at timestamp with time zone, created_at timestamp with time zone, attachment_path_1 text, attachment_path_2 text, attachment_path_3 text, attachment_path_4 text, attachment_path_5 text, group_id bigint)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_message_id bigint;
    v_sent_at timestamptz;
    v_created_at timestamptz;
    v_low bigint;
    v_high bigint;
    v_body text;
    v_path_1 text;
    v_path_2 text;
    v_path_3 text;
    v_path_4 text;
    v_path_5 text;
    v_group_id bigint;
    v_group_chat_id integer;
BEGIN
    IF p_chat_id IS NULL
       OR p_sender_user_id IS NULL
       OR p_message_type_id IS NULL
       OR p_org_id IS NULL
       OR p_app_id IS NULL
       OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_send_message: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    v_body := COALESCE(p_message_body, '');
    v_path_1 := NULLIF(btrim(COALESCE(p_attachment_path_1, '')), '');
    v_path_2 := NULLIF(btrim(COALESCE(p_attachment_path_2, '')), '');
    v_path_3 := NULLIF(btrim(COALESCE(p_attachment_path_3, '')), '');
    v_path_4 := NULLIF(btrim(COALESCE(p_attachment_path_4, '')), '');
    v_path_5 := NULLIF(btrim(COALESCE(p_attachment_path_5, '')), '');

    IF btrim(v_body) = ''
       AND v_path_1 IS NULL
       AND v_path_2 IS NULL
       AND v_path_3 IS NULL
       AND v_path_4 IS NULL
       AND v_path_5 IS NULL THEN
        RAISE EXCEPTION 'fn_chat_send_message: message_body or attachment path is required'
            USING ERRCODE = '22023';
    END IF;

    IF v_path_1 IS NOT NULL AND v_path_1 !~ '^/(uploads|chat-attachments)/' THEN
        RAISE EXCEPTION 'fn_chat_send_message: attachment_path_1 must start with /uploads/ or /chat-attachments/'
            USING ERRCODE = '22023';
    END IF;
    IF v_path_2 IS NOT NULL AND v_path_2 !~ '^/(uploads|chat-attachments)/' THEN
        RAISE EXCEPTION 'fn_chat_send_message: attachment_path_2 must start with /uploads/ or /chat-attachments/'
            USING ERRCODE = '22023';
    END IF;
    IF v_path_3 IS NOT NULL AND v_path_3 !~ '^/(uploads|chat-attachments)/' THEN
        RAISE EXCEPTION 'fn_chat_send_message: attachment_path_3 must start with /uploads/ or /chat-attachments/'
            USING ERRCODE = '22023';
    END IF;
    IF v_path_4 IS NOT NULL AND v_path_4 !~ '^/(uploads|chat-attachments)/' THEN
        RAISE EXCEPTION 'fn_chat_send_message: attachment_path_4 must start with /uploads/ or /chat-attachments/'
            USING ERRCODE = '22023';
    END IF;
    IF v_path_5 IS NOT NULL AND v_path_5 !~ '^/(uploads|chat-attachments)/' THEN
        RAISE EXCEPTION 'fn_chat_send_message: attachment_path_5 must start with /uploads/ or /chat-attachments/'
            USING ERRCODE = '22023';
    END IF;

    -- Resolve group conversation (explicit p_group_id or chat_id mapped on tab_groups).
    v_group_id := p_group_id;
    IF v_group_id IS NULL THEN
        SELECT g.group_id
        INTO v_group_id
        FROM public.tab_groups g
        WHERE g.chat_id = p_chat_id
          AND g.org_id = p_org_id
          AND g.app_id = p_app_id
          AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND COALESCE(g.is_deleted, false) = false
        LIMIT 1;
    END IF;

    IF v_group_id IS NOT NULL THEN
        -- ── GROUP PATH ───────────────────────────────────────────────────────
        SELECT g.chat_id
        INTO v_group_chat_id
        FROM public.tab_groups g
        WHERE g.group_id = v_group_id
          AND g.org_id = p_org_id
          AND g.app_id = p_app_id
          AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND COALESCE(g.is_deleted, false) = false;

        IF v_group_chat_id IS NULL THEN
            RAISE EXCEPTION 'fn_chat_send_message: group % not found or inactive', v_group_id
                USING ERRCODE = 'P0002';
        END IF;

        IF v_group_chat_id IS DISTINCT FROM p_chat_id THEN
            RAISE EXCEPTION 'fn_chat_send_message: chat_id % does not belong to group %',
                p_chat_id, v_group_id
                USING ERRCODE = '22023';
        END IF;

        PERFORM public.fn_chat_assert_group_member(
            v_group_id, p_sender_user_id, p_org_id, p_app_id, p_fiscal_year_id);

        v_sent_at := CURRENT_TIMESTAMP;
        v_created_at := CURRENT_TIMESTAMP;

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
            is_starred_by_receiver,
            attachment_path_1,
            attachment_path_2,
            attachment_path_3,
            attachment_path_4,
            attachment_path_5,
            delete_flag,
            group_id
        )
        VALUES (
            p_chat_id,
            p_sender_user_id,
            NULL,
            v_body,
            p_message_type_id,
            false,
            NULL,
            v_sent_at,
            NULL,
            v_created_at,
            v_created_at,
            p_org_id,
            p_app_id,
            p_fiscal_year_id,
            false,
            false,
            v_path_1,
            v_path_2,
            v_path_3,
            v_path_4,
            v_path_5,
            0,
            v_group_id
        )
        RETURNING public.tab_messages.message_id INTO v_message_id;

        message_id := v_message_id;
        chat_id := p_chat_id;
        sender_user_id := p_sender_user_id;
        receiver_user_id := NULL;
        message_body := v_body;
        message_type_id := p_message_type_id;
        sent_at := v_sent_at;
        created_at := v_created_at;
        attachment_path_1 := v_path_1;
        attachment_path_2 := v_path_2;
        attachment_path_3 := v_path_3;
        attachment_path_4 := v_path_4;
        attachment_path_5 := v_path_5;
        group_id := v_group_id;
        RETURN NEXT;
        RETURN;
    END IF;

    -- ── PRIVATE 1:1 PATH (unchanged rules) ───────────────────────────────────
    IF p_receiver_user_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_send_message: receiver_user_id is required for private chat'
            USING ERRCODE = '22023';
    END IF;

    IF p_sender_user_id = p_receiver_user_id THEN
        RAISE EXCEPTION 'fn_chat_send_message: sender and receiver must be different'
            USING ERRCODE = '22023';
    END IF;

    v_low := LEAST(p_sender_user_id, p_receiver_user_id);
    v_high := GREATEST(p_sender_user_id, p_receiver_user_id);

    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_messages m
        WHERE m.chat_id = p_chat_id
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND m.deleted_at IS NULL
          AND m.group_id IS NULL
          AND (
                m.sender_user_id = p_sender_user_id
             OR m.receiver_user_id = p_sender_user_id
          )
    ) THEN
        RAISE EXCEPTION 'fn_chat_send_message: sender is not a participant of chat_id %', p_chat_id
            USING ERRCODE = '42501';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_messages m
        WHERE m.chat_id = p_chat_id
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND m.deleted_at IS NULL
          AND m.group_id IS NULL
          AND (
                (m.sender_user_id = v_low AND m.receiver_user_id = v_high)
             OR (m.sender_user_id = v_high AND m.receiver_user_id = v_low)
          )
    ) THEN
        RAISE EXCEPTION 'fn_chat_send_message: receiver is not a participant of chat_id %', p_chat_id
            USING ERRCODE = '42501';
    END IF;

    v_sent_at := CURRENT_TIMESTAMP;
    v_created_at := CURRENT_TIMESTAMP;

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
        is_starred_by_receiver,
        attachment_path_1,
        attachment_path_2,
        attachment_path_3,
        attachment_path_4,
        attachment_path_5,
        delete_flag,
        group_id
    )
    VALUES (
        p_chat_id,
        p_sender_user_id,
        p_receiver_user_id,
        v_body,
        p_message_type_id,
        false,
        NULL,
        v_sent_at,
        NULL,
        v_created_at,
        v_created_at,
        p_org_id,
        p_app_id,
        p_fiscal_year_id,
        false,
        false,
        v_path_1,
        v_path_2,
        v_path_3,
        v_path_4,
        v_path_5,
        0,
        NULL
    )
    RETURNING public.tab_messages.message_id INTO v_message_id;

    message_id := v_message_id;
    chat_id := p_chat_id;
    sender_user_id := p_sender_user_id;
    receiver_user_id := p_receiver_user_id;
    message_body := v_body;
    message_type_id := p_message_type_id;
    sent_at := v_sent_at;
    created_at := v_created_at;
    attachment_path_1 := v_path_1;
    attachment_path_2 := v_path_2;
    attachment_path_3 := v_path_3;
    attachment_path_4 := v_path_4;
    attachment_path_5 := v_path_5;
    group_id := NULL;
    RETURN NEXT;
END;
$function$


-- Group Message Info receipts (merged here — no separate SQL file).
CREATE TABLE IF NOT EXISTS public.tab_message_receipts (
    receipt_id      bigserial PRIMARY KEY,
    message_id      bigint NOT NULL,
    user_id         integer NOT NULL,
    delivered_at    timestamptz NULL,
    read_at         timestamptz NULL,
    org_id          integer NOT NULL,
    app_id          integer NOT NULL,
    fiscal_year_id  integer NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_tab_message_receipts_msg_user
        UNIQUE (message_id, user_id, org_id, app_id, fiscal_year_id)
);

CREATE INDEX IF NOT EXISTS ix_tab_message_receipts_message
    ON public.tab_message_receipts (message_id);

CREATE INDEX IF NOT EXISTS ix_tab_message_receipts_user
    ON public.tab_message_receipts (user_id);

DROP FUNCTION IF EXISTS public.fn_chat_get_message_info(bigint, bigint, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_get_message_info(
    p_message_id bigint,
    p_user_id bigint,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer
)
RETURNS TABLE (
    message_id bigint,
    chat_id integer,
    group_id bigint,
    sender_user_id bigint,
    sent_at timestamp with time zone,
    member_user_id bigint,
    delivered_at timestamp with time zone,
    read_at timestamp with time zone
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
DECLARE
    v_msg public.tab_messages%ROWTYPE;
    v_group_id bigint;
BEGIN
    IF p_message_id IS NULL OR p_user_id IS NULL
       OR p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_get_message_info: required parameters cannot be null'
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
        RAISE EXCEPTION 'fn_chat_get_message_info: message % not found', p_message_id
            USING ERRCODE = 'P0002';
    END IF;

    IF COALESCE(v_msg.delete_flag, 0) <> 0 OR v_msg.deleted_at IS NOT NULL THEN
        RAISE EXCEPTION 'fn_chat_get_message_info: message % is deleted', p_message_id
            USING ERRCODE = '22023';
    END IF;

    v_group_id := v_msg.group_id;
    IF v_group_id IS NULL THEN
        SELECT g.group_id
        INTO v_group_id
        FROM public.tab_groups g
        WHERE g.chat_id = v_msg.chat_id
          AND g.org_id = p_org_id
          AND g.app_id = p_app_id
          AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND COALESCE(g.is_deleted, false) = false
        LIMIT 1;
    END IF;

    IF v_group_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_get_message_info: message % is not a group message', p_message_id
            USING ERRCODE = '22023';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_group_members gm
        WHERE gm.group_id = v_group_id
          AND gm.user_id = p_user_id::integer
          AND gm.left_at IS NULL
          AND gm.org_id = p_org_id
          AND gm.app_id = p_app_id
    ) THEN
        RAISE EXCEPTION 'fn_chat_get_message_info: user % is not an active member of group %',
            p_user_id, v_group_id
            USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    SELECT
        v_msg.message_id,
        v_msg.chat_id,
        v_group_id,
        v_msg.sender_user_id,
        v_msg.sent_at,
        gm.user_id::bigint AS member_user_id,
        r.delivered_at,
        r.read_at
    FROM public.tab_group_members gm
    LEFT JOIN LATERAL (
        SELECT rr.delivered_at, rr.read_at
        FROM public.tab_message_receipts rr
        WHERE rr.message_id = v_msg.message_id
          AND rr.user_id = gm.user_id
          AND rr.org_id = p_org_id
          AND rr.app_id = p_app_id
        ORDER BY rr.read_at DESC NULLS LAST, rr.delivered_at DESC NULLS LAST
        LIMIT 1
    ) r ON TRUE
    WHERE gm.group_id = v_group_id
      AND gm.left_at IS NULL
      AND gm.org_id = p_org_id
      AND gm.app_id = p_app_id
      AND gm.user_id IS DISTINCT FROM v_msg.sender_user_id::integer
    ORDER BY
        CASE WHEN r.read_at IS NOT NULL THEN 0
             WHEN r.delivered_at IS NOT NULL THEN 1
             ELSE 2 END,
        COALESCE(r.read_at, r.delivered_at) DESC NULLS LAST,
        gm.user_id;

    IF NOT FOUND THEN
        message_id := v_msg.message_id;
        chat_id := v_msg.chat_id;
        group_id := v_group_id;
        sender_user_id := v_msg.sender_user_id;
        sent_at := v_msg.sent_at;
        member_user_id := NULL;
        delivered_at := NULL;
        read_at := NULL;
        RETURN NEXT;
    END IF;
END;
$fn$;

CREATE OR REPLACE FUNCTION public.fn_chat_mark_messages_read(
    p_chat_id integer,
    p_reader_user_id bigint,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer
)
RETURNS TABLE(
    message_id bigint,
    chat_id integer,
    reader_user_id bigint,
    sender_user_id bigint,
    read_at timestamp with time zone
)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_read_at timestamptz;
    v_group_id bigint;
BEGIN
    IF p_chat_id IS NULL OR p_chat_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_mark_messages_read: chat_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_reader_user_id IS NULL OR p_reader_user_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_mark_messages_read: reader_user_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_mark_messages_read: org_id, app_id and fiscal_year_id are required'
            USING ERRCODE = '22023';
    END IF;

    v_read_at := CURRENT_TIMESTAMP;

    SELECT g.group_id
    INTO v_group_id
    FROM public.tab_groups g
    WHERE g.chat_id = p_chat_id
      AND g.org_id = p_org_id
      AND g.app_id = p_app_id
      AND COALESCE(g.is_deleted, false) = false
    ORDER BY CASE
                 WHEN g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id THEN 0
                 ELSE 1
             END
    LIMIT 1;

    IF v_group_id IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1
            FROM public.tab_group_members gm
            WHERE gm.group_id = v_group_id
              AND gm.user_id = p_reader_user_id::integer
              AND gm.left_at IS NULL
              AND gm.org_id = p_org_id
              AND gm.app_id = p_app_id
        ) THEN
            RAISE EXCEPTION 'fn_chat_mark_messages_read: reader is not an active member of group %',
                v_group_id
                USING ERRCODE = '42501';
        END IF;

        RETURN QUERY
        WITH target AS (
            SELECT m.message_id, m.chat_id, m.sender_user_id
            FROM public.tab_messages m
            WHERE m.chat_id = p_chat_id
              AND m.org_id = p_org_id
              AND m.app_id = p_app_id
              AND m.deleted_at IS NULL
              AND COALESCE(m.delete_flag, 0) = 0
              AND COALESCE(m.is_draft, false) = false
              AND m.sent_at IS NOT NULL
              AND m.sender_user_id IS DISTINCT FROM p_reader_user_id
              AND (
                    m.group_id IS NOT DISTINCT FROM v_group_id
                 OR m.group_id IS NULL
              )
              AND NOT EXISTS (
                  SELECT 1
                  FROM public.tab_message_receipts r
                  WHERE r.message_id = m.message_id
                    AND r.user_id = p_reader_user_id::integer
                    AND r.org_id = p_org_id
                    AND r.app_id = p_app_id
                    AND r.read_at IS NOT NULL
              )
        ),
        upserted AS (
            INSERT INTO public.tab_message_receipts (
                message_id, user_id, delivered_at, read_at,
                org_id, app_id, fiscal_year_id, created_at, updated_at
            )
            SELECT
                t.message_id,
                p_reader_user_id::integer,
                v_read_at,
                v_read_at,
                p_org_id,
                p_app_id,
                p_fiscal_year_id,
                v_read_at,
                v_read_at
            FROM target t
            ON CONFLICT (message_id, user_id, org_id, app_id, fiscal_year_id)
            DO UPDATE SET
                delivered_at = COALESCE(public.tab_message_receipts.delivered_at, EXCLUDED.delivered_at),
                read_at = COALESCE(public.tab_message_receipts.read_at, EXCLUDED.read_at),
                updated_at = v_read_at
            WHERE public.tab_message_receipts.read_at IS NULL
            RETURNING
                public.tab_message_receipts.message_id,
                public.tab_message_receipts.read_at
        )
        SELECT
            u.message_id,
            p_chat_id,
            p_reader_user_id,
            t.sender_user_id,
            u.read_at
        FROM upserted u
        INNER JOIN target t ON t.message_id = u.message_id;

        RETURN;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_messages m
        WHERE m.chat_id = p_chat_id
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND m.deleted_at IS NULL
          AND (
                m.sender_user_id = p_reader_user_id
             OR m.receiver_user_id = p_reader_user_id
          )
    ) THEN
        RAISE EXCEPTION 'fn_chat_mark_messages_read: reader is not a participant of chat_id %', p_chat_id
            USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    WITH updated AS (
        UPDATE public.tab_messages m
        SET read_at = v_read_at,
            updated_at = v_read_at
        WHERE m.chat_id = p_chat_id
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND m.deleted_at IS NULL
          AND COALESCE(m.delete_flag, 0) = 0
          AND COALESCE(m.is_draft, false) = false
          AND m.receiver_user_id = p_reader_user_id
          AND m.sender_user_id IS DISTINCT FROM p_reader_user_id
          AND m.read_at IS NULL
          AND m.sent_at IS NOT NULL
          AND m.group_id IS NULL
        RETURNING m.message_id, m.chat_id, m.sender_user_id, m.read_at
    )
    SELECT
        u.message_id,
        u.chat_id,
        p_reader_user_id,
        u.sender_user_id,
        u.read_at
    FROM updated u;
END;
$function$;


ALTER TABLE public.tab_messages
    ALTER COLUMN read_at DROP NOT NULL;
