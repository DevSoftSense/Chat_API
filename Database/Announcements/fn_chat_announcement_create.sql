-- fn_chat_announcement_create (SoftOnCloud tab_announcements + recipients)
-- DROP required when RETURNS TABLE gains recipient_user_ids.

DROP FUNCTION IF EXISTS public.fn_chat_announcement_create(
    VARCHAR, TEXT, INTEGER, INTEGER, VARCHAR, VARCHAR, BOOLEAN, INTEGER,
    TIMESTAMPTZ, BIGINT, VARCHAR, INTEGER, INTEGER, INTEGER, BIGINT[]
);

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_create(
    p_title              VARCHAR,
    p_description        TEXT,
    p_category_id        INTEGER,
    p_department_id      INTEGER,
    p_department_name    VARCHAR,
    p_location           VARCHAR,
    p_is_important       BOOLEAN,
    p_status_id          INTEGER,
    p_expiry_date        TIMESTAMPTZ,
    p_posted_by          BIGINT,
    p_posted_by_name     VARCHAR,
    p_org_id             INTEGER,
    p_app_id             INTEGER,
    p_fiscal_year_id     INTEGER,
    p_recipient_user_ids BIGINT[]
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
    v_id BIGINT;
    v_status_name VARCHAR(50);
    v_recipients BIGINT[];
    v_department_id BIGINT;
    v_location VARCHAR(255);
    v_is_important BOOLEAN;
BEGIN
    IF p_posted_by IS NULL OR p_posted_by <= 0 THEN
        RAISE EXCEPTION 'postedBy is required' USING ERRCODE = '22023';
    END IF;
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
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

    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_announcement_category_master c
        WHERE c.category_id = p_category_id::SMALLINT
          AND c.org_id = p_org_id
          AND c.app_id = p_app_id
          AND c.is_active
    ) THEN
        RAISE EXCEPTION 'Invalid categoryId' USING ERRCODE = '22023';
    END IF;

    v_is_important := COALESCE(p_is_important, FALSE);
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

    -- Status stays Active / Expiring / Expired; Important is a separate flag.
    IF p_expiry_date IS NOT NULL AND p_expiry_date < NOW() THEN
        v_status_name := 'Expired';
    ELSIF p_expiry_date IS NOT NULL AND p_expiry_date <= (NOW() + INTERVAL '7 days') THEN
        v_status_name := 'Expiring Soon';
    ELSE
        v_status_name := CASE COALESCE(p_status_id, 1)
            WHEN 2 THEN 'Expiring Soon'
            WHEN 3 THEN 'Expired'
            ELSE 'Active'
        END;
    END IF;

    v_recipients := ARRAY(
        SELECT DISTINCT x
        FROM unnest(COALESCE(p_recipient_user_ids, ARRAY[]::BIGINT[])) AS x
        WHERE x > 0
    );
    IF NOT (p_posted_by = ANY (v_recipients)) THEN
        v_recipients := array_append(v_recipients, p_posted_by);
    END IF;

    INSERT INTO public.tab_announcements (
        title,
        description,
        category_id,
        status_name,
        posted_by,
        department_id,
        location,
        is_important,
        is_pinned,
        published_at,
        expires_at,
        org_id,
        app_id,
        fiscal_year_id,
        delete_flag,
        delete_at
    )
    VALUES (
        TRIM(p_title),
        TRIM(p_description),
        p_category_id::SMALLINT,
        v_status_name,
        p_posted_by,
        v_department_id,
        v_location,
        v_is_important,
        FALSE,
        NOW(),
        p_expiry_date,
        p_org_id,
        p_app_id,
        p_fiscal_year_id,
        0::SMALLINT,
        NULL
    )
    RETURNING tab_announcements.announcement_id INTO v_id;

    INSERT INTO public.tab_announcement_recipients (
        announcement_id,
        user_id,
        org_id,
        app_id,
        read_status,
        read_on,
        fiscal_year_id
    )
    SELECT
        v_id,
        uid::INTEGER,
        p_org_id,
        p_app_id,
        CASE WHEN uid = p_posted_by THEN 'read' ELSE 'unread' END,
        CASE WHEN uid = p_posted_by THEN NOW() ELSE NULL END,
        p_fiscal_year_id
    FROM unnest(v_recipients) AS uid
    ON CONFLICT ON CONSTRAINT tab_announcement_recipients_announcement_id_user_id_key DO NOTHING;

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
    FROM public.fn_chat_announcement_get_by_id(v_id, p_posted_by, p_org_id, p_app_id) AS g;
END;
$$;
