-- Uses existing public.tab_messages only.
-- Forwards by INSERTING a new row (never updates the original).
-- Supports private destination OR group destination (via tab_groups.chat_id).

-- Required columns (safe if already present).
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'tab_messages' AND column_name = 'group_id'
    ) THEN
        ALTER TABLE public.tab_messages ADD COLUMN group_id bigint NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'tab_messages' AND column_name = 'forwarded_from_message_id'
    ) THEN
        ALTER TABLE public.tab_messages ADD COLUMN forwarded_from_message_id bigint NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'tab_messages' AND column_name = 'forwarded_by'
    ) THEN
        ALTER TABLE public.tab_messages ADD COLUMN forwarded_by bigint NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_tab_messages_group_id
    ON public.tab_messages (group_id)
    WHERE group_id IS NOT NULL;

DROP FUNCTION IF EXISTS public.fn_chat_forward_message(bigint, integer, bigint, bigint, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_forward_message(
    p_original_message_id bigint,
    p_destination_chat_id integer,
    p_forwarded_by bigint,
    p_receiver_user_id bigint,
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
    sent_at timestamp with time zone,
    created_at timestamp with time zone,
    attachment_path_1 text,
    attachment_path_2 text,
    attachment_path_3 text,
    attachment_path_4 text,
    attachment_path_5 text,
    forwarded_from_message_id bigint,
    forwarded_by bigint,
    group_id bigint
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
DECLARE
    v_orig public.tab_messages%ROWTYPE;
    v_message_id bigint;
    v_sent_at timestamptz;
    v_created_at timestamptz;
    v_dest_group_id bigint;
    v_orig_group_id bigint;
    v_can_forward boolean := false;
BEGIN
    IF p_original_message_id IS NULL
       OR p_destination_chat_id IS NULL
       OR p_forwarded_by IS NULL
       OR p_org_id IS NULL
       OR p_app_id IS NULL
       OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_forward_message: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    SELECT *
    INTO v_orig
    FROM public.tab_messages m
    WHERE m.message_id = p_original_message_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND COALESCE(m.is_draft, false) = false
    ORDER BY CASE
                 WHEN m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id THEN 0
                 ELSE 1
             END
    LIMIT 1;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'fn_chat_forward_message: original message % not found', p_original_message_id
            USING ERRCODE = 'P0002';
    END IF;

    IF COALESCE(v_orig.delete_flag, 0) <> 0 OR v_orig.deleted_at IS NOT NULL THEN
        RAISE EXCEPTION 'fn_chat_forward_message: original message % is deleted', p_original_message_id
            USING ERRCODE = '22023';
    END IF;

    -- May forward if private participant OR active member of original group.
    IF v_orig.sender_user_id IS NOT DISTINCT FROM p_forwarded_by
       OR v_orig.receiver_user_id IS NOT DISTINCT FROM p_forwarded_by THEN
        v_can_forward := true;
    END IF;

    v_orig_group_id := v_orig.group_id;
    IF v_orig_group_id IS NULL THEN
        SELECT g.group_id
        INTO v_orig_group_id
        FROM public.tab_groups g
        WHERE g.chat_id = v_orig.chat_id
          AND COALESCE(g.is_deleted, false) = false
        ORDER BY CASE
                     WHEN g.org_id = p_org_id AND g.app_id = p_app_id THEN 0
                     ELSE 1
                 END
        LIMIT 1;
    END IF;

    IF NOT v_can_forward AND v_orig_group_id IS NOT NULL THEN
        IF EXISTS (
            SELECT 1
            FROM public.tab_group_members gm
            WHERE gm.group_id = v_orig_group_id
              AND gm.user_id = p_forwarded_by::integer
              AND gm.left_at IS NULL
        ) THEN
            v_can_forward := true;
        END IF;
    END IF;

    IF NOT v_can_forward THEN
        RAISE EXCEPTION 'fn_chat_forward_message: user % is not allowed to forward message %',
            p_forwarded_by, p_original_message_id
            USING ERRCODE = '42501';
    END IF;

    v_sent_at := CURRENT_TIMESTAMP;
    v_created_at := CURRENT_TIMESTAMP;

    -- Private destination wins when receiver is provided (avoids chat_id collision
    -- with tab_groups when private + group share the same id space).
    IF p_receiver_user_id IS NOT NULL AND p_receiver_user_id > 0 THEN
        IF p_forwarded_by = p_receiver_user_id THEN
            RAISE EXCEPTION 'fn_chat_forward_message: sender and receiver must be different'
                USING ERRCODE = '22023';
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
            is_starred_by_receiver,
            attachment_path_1,
            attachment_path_2,
            attachment_path_3,
            attachment_path_4,
            attachment_path_5,
            forwarded_from_message_id,
            forwarded_by,
            delete_flag,
            group_id
        )
        VALUES (
            p_destination_chat_id,
            p_forwarded_by,
            p_receiver_user_id,
            v_orig.message_body,
            v_orig.message_type_id,
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
            v_orig.attachment_path_1,
            v_orig.attachment_path_2,
            v_orig.attachment_path_3,
            v_orig.attachment_path_4,
            v_orig.attachment_path_5,
            v_orig.message_id,
            p_forwarded_by,
            0,
            NULL
        )
        RETURNING public.tab_messages.message_id INTO v_message_id;

        message_id := v_message_id;
        chat_id := p_destination_chat_id;
        sender_user_id := p_forwarded_by;
        receiver_user_id := p_receiver_user_id;
        message_body := v_orig.message_body;
        message_type_id := v_orig.message_type_id;
        sent_at := v_sent_at;
        created_at := v_created_at;
        attachment_path_1 := v_orig.attachment_path_1::text;
        attachment_path_2 := v_orig.attachment_path_2::text;
        attachment_path_3 := v_orig.attachment_path_3::text;
        attachment_path_4 := v_orig.attachment_path_4::text;
        attachment_path_5 := v_orig.attachment_path_5::text;
        forwarded_from_message_id := v_orig.message_id;
        forwarded_by := p_forwarded_by;
        group_id := NULL;
        RETURN NEXT;
        RETURN;
    END IF;

    -- Group destination (chat_id on tab_groups) — only when no private receiver.
    SELECT g.group_id
    INTO v_dest_group_id
    FROM public.tab_groups g
    WHERE g.chat_id = p_destination_chat_id
      AND COALESCE(g.is_deleted, false) = false
    ORDER BY CASE
                 WHEN g.org_id = p_org_id AND g.app_id = p_app_id
                      AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id THEN 0
                 WHEN g.org_id = p_org_id AND g.app_id = p_app_id THEN 1
                 ELSE 2
             END
    LIMIT 1;

    IF v_dest_group_id IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1
            FROM public.tab_group_members gm
            WHERE gm.group_id = v_dest_group_id
              AND gm.user_id = p_forwarded_by::integer
              AND gm.left_at IS NULL
        ) THEN
            RAISE EXCEPTION 'fn_chat_forward_message: user % is not an active member of destination group %',
                p_forwarded_by, v_dest_group_id
                USING ERRCODE = '42501';
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
            is_starred_by_receiver,
            attachment_path_1,
            attachment_path_2,
            attachment_path_3,
            attachment_path_4,
            attachment_path_5,
            forwarded_from_message_id,
            forwarded_by,
            delete_flag,
            group_id
        )
        VALUES (
            p_destination_chat_id,
            p_forwarded_by,
            NULL,
            v_orig.message_body,
            v_orig.message_type_id,
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
            v_orig.attachment_path_1,
            v_orig.attachment_path_2,
            v_orig.attachment_path_3,
            v_orig.attachment_path_4,
            v_orig.attachment_path_5,
            v_orig.message_id,
            p_forwarded_by,
            0,
            v_dest_group_id
        )
        RETURNING public.tab_messages.message_id INTO v_message_id;

        message_id := v_message_id;
        chat_id := p_destination_chat_id;
        sender_user_id := p_forwarded_by;
        receiver_user_id := NULL;
        message_body := v_orig.message_body;
        message_type_id := v_orig.message_type_id;
        sent_at := v_sent_at;
        created_at := v_created_at;
        attachment_path_1 := v_orig.attachment_path_1::text;
        attachment_path_2 := v_orig.attachment_path_2::text;
        attachment_path_3 := v_orig.attachment_path_3::text;
        attachment_path_4 := v_orig.attachment_path_4::text;
        attachment_path_5 := v_orig.attachment_path_5::text;
        forwarded_from_message_id := v_orig.message_id;
        forwarded_by := p_forwarded_by;
        group_id := v_dest_group_id;
        RETURN NEXT;
        RETURN;
    END IF;

    RAISE EXCEPTION 'fn_chat_forward_message: destination chat % is not a group and receiver_user_id was not provided',
        p_destination_chat_id
        USING ERRCODE = '22023';
END;
$fn$;

COMMENT ON FUNCTION public.fn_chat_forward_message(bigint, integer, bigint, bigint, integer, integer, integer)
IS 'Inserts a new forwarded message into private or group chat; original row is never updated.';
