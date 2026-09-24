-- fn_chat_announcement_mark_read (idempotent)

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_mark_read(
    p_announcement_id BIGINT,
    p_user_id         BIGINT,
    p_org_id          INTEGER,
    p_app_id          INTEGER
)
RETURNS TABLE (
    announcement_id BIGINT,
    is_read         BOOLEAN,
    read_at         TIMESTAMPTZ
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_exists BOOLEAN;
    v_read_at TIMESTAMPTZ;
    v_fiscal INTEGER;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM public.tab_announcements a
        WHERE a.announcement_id = p_announcement_id
          AND a.org_id = p_org_id
          AND a.app_id = p_app_id
          AND COALESCE(a.delete_flag, 0) = 0
          AND LOWER(TRIM(a.status_name)) <> 'deleted'
    ),
    (
        SELECT a.fiscal_year_id FROM public.tab_announcements a
        WHERE a.announcement_id = p_announcement_id
        LIMIT 1
    )
    INTO v_exists, v_fiscal;

    IF NOT v_exists THEN
        RAISE EXCEPTION 'Announcement not found' USING ERRCODE = 'P0002';
    END IF;

    INSERT INTO public.tab_announcement_recipients (
        announcement_id, user_id, org_id, app_id, read_status, read_on, fiscal_year_id
    )
    VALUES (
        p_announcement_id,
        p_user_id::INTEGER,
        p_org_id,
        p_app_id,
        'read',
        NOW(),
        v_fiscal
    )
    ON CONFLICT ON CONSTRAINT tab_announcement_recipients_announcement_id_user_id_key DO UPDATE
    SET
        read_status = 'read',
        read_on = COALESCE(public.tab_announcement_recipients.read_on, EXCLUDED.read_on)
    RETURNING public.tab_announcement_recipients.read_on INTO v_read_at;

    RETURN QUERY SELECT p_announcement_id, TRUE, v_read_at;
END;
$$;
