-- fn_chat_announcement_update
-- DROP required when RETURNS TABLE gains recipient_user_ids.

DROP FUNCTION IF EXISTS public.fn_chat_announcement_update(
    BIGINT, BIGINT, INTEGER, INTEGER, VARCHAR, TEXT, INTEGER, INTEGER,
    VARCHAR, VARCHAR, BOOLEAN, INTEGER, TIMESTAMPTZ, BIGINT[]
);

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_update(
    p_announcement_id    BIGINT,
    p_user_id            BIGINT,
    p_org_id             INTEGER,
    p_app_id             INTEGER,
    p_title              VARCHAR,
    p_description        TEXT,
    p_category_id        INTEGER,
    p_department_id      INTEGER,
    p_department_name    VARCHAR,
    p_location           VARCHAR,
    p_is_important       BOOLEAN,
    p_status_id          INTEGER,
    p_expiry_date        TIMESTAMPTZ,
    p_recipient_user_ids BIGINT[] DEFAULT NULL
)
RETURNS TABLE (
    announcement_id       BIGINT,
    title                 VARCHAR,
    description           TEXT,
    category_id           INTEGER,
    category_name         VARCHAR,
    department_id         INTEGER,
    department_name       VARCHAR,
    location              VARCHAR,
    is_important          BOOLEAN,
    is_pinned             BOOLEAN,
    status_id             INTEGER,
    status_name           VARCHAR,
    posted_by             BIGINT,
    posted_by_name        VARCHAR,
    posted_date           TIMESTAMPTZ,
    expiry_date           TIMESTAMPTZ,
    read_count            INTEGER,
    recipient_count       INTEGER,
    is_read_by_current_user BOOLEAN,
    recipient_user_ids    BIGINT[]
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_posted_by BIGINT;
    v_recipients BIGINT[];
    v_status_name VARCHAR(50);
    v_fiscal INTEGER;
    v_department_id BIGINT;
    v_location VARCHAR(255);
    v_is_important BOOLEAN;
BEGIN
    SELECT a.posted_by, a.fiscal_year_id, a.status_name, a.is_important
    INTO v_posted_by, v_fiscal, v_status_name, v_is_important
    FROM public.tab_announcements a
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
      AND COALESCE(a.delete_flag, 0) = 0
      AND LOWER(TRIM(a.status_name)) <> 'deleted';

    IF v_posted_by IS NULL THEN
        RAISE EXCEPTION 'Announcement not found' USING ERRCODE = 'P0002';
    END IF;
    IF v_posted_by <> p_user_id THEN
        RAISE EXCEPTION 'Only the poster can update this announcement' USING ERRCODE = '42501';
    END IF;
    IF NULLIF(TRIM(COALESCE(p_title, '')), '') IS NULL THEN
        RAISE EXCEPTION 'title is required' USING ERRCODE = '22023';
    END IF;
    IF NULLIF(TRIM(COALESCE(p_description, '')), '') IS NULL THEN
        RAISE EXCEPTION 'description is required' USING ERRCODE = '22023';
    END IF;
    IF p_category_id IS NULL OR p_category_id <= 0 THEN
        RAISE EXCEPTION 'categoryId is required' USING ERRCODE = '22023';
    END IF;

    v_is_important := COALESCE(p_is_important, v_is_important, FALSE);
    v_location := NULLIF(TRIM(COALESCE(p_location, '')), '');

    v_department_id := CASE
        WHEN p_department_id IS NOT NULL AND p_department_id > 0 THEN p_department_id::BIGINT
        ELSE NULL
    END;

    IF v_department_id IS NULL AND NULLIF(TRIM(COALESCE(p_department_name, '')), '') IS NOT NULL THEN
        SELECT d.department_id::BIGINT
        INTO v_department_id
        FROM public.tab_department_master d
        WHERE LOWER(TRIM(d.department_name)) = LOWER(TRIM(p_department_name))
        ORDER BY d.department_id
        LIMIT 1;
    END IF;

    IF p_expiry_date IS NOT NULL AND p_expiry_date < NOW() THEN
        v_status_name := 'Expired';
    ELSIF p_expiry_date IS NOT NULL AND p_expiry_date <= (NOW() + INTERVAL '7 days') THEN
        v_status_name := 'Expiring Soon';
    ELSE
        -- Future expiry, cleared expiry, or no expiry → Active (never leave stale Expired)
        v_status_name := 'Active';
    END IF;

    UPDATE public.tab_announcements a
    SET
        title = TRIM(p_title),
        description = TRIM(p_description),
        category_id = p_category_id::SMALLINT,
        department_id = v_department_id,
        location = v_location,
        is_important = v_is_important,
        status_name = v_status_name,
        expires_at = p_expiry_date,
        updated_at = NOW()
    WHERE a.announcement_id = p_announcement_id;

    IF p_recipient_user_ids IS NOT NULL THEN
        v_recipients := ARRAY(
            SELECT DISTINCT x FROM unnest(p_recipient_user_ids) AS x WHERE x > 0
        );
        IF NOT (p_user_id = ANY (v_recipients)) THEN
            v_recipients := array_append(v_recipients, p_user_id);
        END IF;

        DELETE FROM public.tab_announcement_recipients r
        WHERE r.announcement_id = p_announcement_id
          AND NOT (r.user_id::BIGINT = ANY (v_recipients));

        INSERT INTO public.tab_announcement_recipients (
            announcement_id, user_id, org_id, app_id, read_status, read_on, fiscal_year_id
        )
        SELECT
            p_announcement_id,
            uid::INTEGER,
            p_org_id,
            p_app_id,
            CASE WHEN uid = p_user_id THEN 'read' ELSE 'unread' END,
            CASE WHEN uid = p_user_id THEN NOW() ELSE NULL END,
            v_fiscal
        FROM unnest(v_recipients) AS uid
        ON CONFLICT ON CONSTRAINT tab_announcement_recipients_announcement_id_user_id_key DO NOTHING;
    END IF;

    -- Alias columns: RETURNS TABLE(announcement_id ...) makes bare SELECT * ambiguous.
    RETURN QUERY
    SELECT
        g.announcement_id,
        g.title,
        g.description,
        g.category_id,
        g.category_name,
        g.department_id,
        g.department_name,
        g.location,
        g.is_important,
        g.is_pinned,
        g.status_id,
        g.status_name,
        g.posted_by,
        g.posted_by_name,
        g.posted_date,
        g.expiry_date,
        g.read_count,
        g.recipient_count,
        g.is_read_by_current_user,
        g.recipient_user_ids
    FROM public.fn_chat_announcement_get_by_id(
        p_announcement_id, p_user_id, p_org_id, p_app_id
    ) AS g;
END;
$$;
