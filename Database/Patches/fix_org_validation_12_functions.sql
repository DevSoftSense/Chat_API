-- ============================================================
-- Patch: Organization ID validation for 12 chat functions
-- Run this on your PostgreSQL chat DB (live / local).
-- Adds: IF p_org_id IS NULL OR p_org_id <= 0 ... RAISE
-- ============================================================


-- >>> fn_chat_announcement_delete.sql

-- fn_chat_announcement_delete
-- Soft-delete ONLY: delete_flag = 1, delete_at = now()
-- NEVER physically deletes the row / recipients.

DROP FUNCTION IF EXISTS public.fn_chat_announcement_delete(BIGINT, BIGINT, INTEGER, INTEGER);

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_delete(
    p_announcement_id BIGINT,
    p_user_id         BIGINT,
    p_org_id          INTEGER,
    p_app_id          INTEGER
)
RETURNS TABLE (
    result_announcement_id BIGINT,
    result_deleted         BOOLEAN
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_posted_by BIGINT;
    v_delete_at TIMESTAMPTZ;
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    SELECT a.posted_by INTO v_posted_by
    FROM public.tab_announcements a
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
      AND COALESCE(a.delete_flag, 0) = 0;

    IF v_posted_by IS NULL THEN
        RAISE EXCEPTION 'Announcement not found' USING ERRCODE = 'P0002';
    END IF;
    IF v_posted_by <> p_user_id THEN
        RAISE EXCEPTION 'Only the poster can delete this announcement' USING ERRCODE = '42501';
    END IF;

    v_delete_at := CURRENT_TIMESTAMP;

    UPDATE public.tab_announcements a
    SET delete_flag = 1::SMALLINT,
        delete_at   = v_delete_at,
        updated_at  = v_delete_at
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
      AND COALESCE(a.delete_flag, 0) = 0;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Announcement not found' USING ERRCODE = 'P0002';
    END IF;

    result_announcement_id := p_announcement_id;
    result_deleted := TRUE;
    RETURN NEXT;
END;
$$;

COMMENT ON FUNCTION public.fn_chat_announcement_delete(BIGINT, BIGINT, INTEGER, INTEGER)
IS 'Soft-deletes an announcement (delete_flag=1 + delete_at). Never physically deletes the row.';




-- >>> fn_chat_announcement_get_by_id.sql

-- fn_chat_announcement_get_by_id
-- DROP required when RETURNS TABLE gains recipient_user_ids.

DROP FUNCTION IF EXISTS public.fn_chat_announcement_get_by_id(BIGINT, BIGINT, INTEGER, INTEGER);

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_get_by_id(
    p_announcement_id BIGINT,
    p_user_id         BIGINT,
    p_org_id          INTEGER,
    p_app_id          INTEGER
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
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;
    IF p_announcement_id IS NULL OR p_announcement_id <= 0 THEN
        RAISE EXCEPTION 'announcementId is required' USING ERRCODE = '22023';
    END IF;
    IF p_user_id IS NULL OR p_user_id <= 0 THEN
        RAISE EXCEPTION 'userId is required' USING ERRCODE = '22023';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_announcements a
        WHERE a.announcement_id = p_announcement_id
          AND a.org_id = p_org_id
          AND a.app_id = p_app_id
          AND COALESCE(a.delete_flag, 0) = 0
          AND LOWER(TRIM(a.status_name)) <> 'deleted'
          AND (
              a.posted_by = p_user_id
              OR EXISTS (
                  SELECT 1
                  FROM public.tab_announcement_recipients r
                  WHERE r.announcement_id = a.announcement_id
                    AND r.user_id::BIGINT = p_user_id
              )
          )
    ) THEN
        RAISE EXCEPTION 'Announcement not found' USING ERRCODE = 'P0002';
    END IF;

    RETURN QUERY
    SELECT
        a.announcement_id,
        a.title::VARCHAR,
        a.description,
        a.category_id::INTEGER,
        cat.category_name::VARCHAR,
        a.department_id::INTEGER,
        dept.department_name::VARCHAR,
        a.location::VARCHAR,
        COALESCE(a.is_important, LOWER(TRIM(a.status_name)) = 'important') AS is_important,
        a.is_pinned,
        CASE
            WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 3
            WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 2
            ELSE 1
        END AS status_id,
        CASE
            WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 'Expired'::VARCHAR
            WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 'Expiring Soon'::VARCHAR
            ELSE 'Active'::VARCHAR
        END AS status_name,
        a.posted_by,
        NULL::VARCHAR AS posted_by_name,
        COALESCE(a.published_at, a.created_at) AS posted_date,
        a.expires_at AS expiry_date,
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcement_recipients r
            WHERE r.announcement_id = a.announcement_id
              AND LOWER(TRIM(r.read_status)) = 'read'
        ) AS read_count,
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcement_recipients r
            WHERE r.announcement_id = a.announcement_id
        ) AS recipient_count,
        COALESCE((
            SELECT LOWER(TRIM(r.read_status)) = 'read'
            FROM public.tab_announcement_recipients r
            WHERE r.announcement_id = a.announcement_id
              AND r.user_id::BIGINT = p_user_id
            LIMIT 1
        ), FALSE) AS is_read_by_current_user,
        COALESCE((
            SELECT array_agg(r.user_id::BIGINT ORDER BY r.user_id)
            FROM public.tab_announcement_recipients r
            WHERE r.announcement_id = a.announcement_id
        ), ARRAY[]::BIGINT[]) AS recipient_user_ids
    FROM public.tab_announcements a
    LEFT JOIN public.tab_announcement_category_master cat
        ON cat.category_id = a.category_id
    LEFT JOIN public.tab_department_master dept
        ON dept.department_id = a.department_id::INTEGER
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
      AND COALESCE(a.delete_flag, 0) = 0;
END;
$$;




-- >>> fn_chat_announcement_get_stats.sql

-- fn_chat_announcement_get_stats + fn_chat_announcement_unread_count

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_get_stats(
    p_user_id BIGINT,
    p_org_id  INTEGER,
    p_app_id  INTEGER
)
RETURNS TABLE (
    total_announcements INTEGER,
    total_reads         INTEGER,
    unread_by_me        INTEGER
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcements a
            WHERE a.org_id = p_org_id
              AND a.app_id = p_app_id
              AND COALESCE(a.delete_flag, 0) = 0
              AND LOWER(TRIM(a.status_name)) <> 'deleted'
              AND (
                  a.posted_by = p_user_id
                  OR EXISTS (
                      SELECT 1 FROM public.tab_announcement_recipients r
                      WHERE r.announcement_id = a.announcement_id
                        AND r.user_id::BIGINT = p_user_id
                  )
              )
        ) AS total_announcements,
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcement_recipients r
            INNER JOIN public.tab_announcements a ON a.announcement_id = r.announcement_id
            WHERE a.org_id = p_org_id
              AND a.app_id = p_app_id
              AND COALESCE(a.delete_flag, 0) = 0
              AND LOWER(TRIM(a.status_name)) <> 'deleted'
              AND LOWER(TRIM(r.read_status)) = 'read'
              AND (
                  a.posted_by = p_user_id
                  OR EXISTS (
                      SELECT 1 FROM public.tab_announcement_recipients r2
                      WHERE r2.announcement_id = a.announcement_id
                        AND r2.user_id::BIGINT = p_user_id
                  )
              )
        ) AS total_reads,
        (
            SELECT COUNT(*)::INTEGER
            FROM public.tab_announcement_recipients r
            INNER JOIN public.tab_announcements a ON a.announcement_id = r.announcement_id
            WHERE r.user_id::BIGINT = p_user_id
              AND LOWER(TRIM(r.read_status)) <> 'read'
              AND a.org_id = p_org_id
              AND a.app_id = p_app_id
              AND COALESCE(a.delete_flag, 0) = 0
              AND LOWER(TRIM(a.status_name)) <> 'deleted'
        ) AS unread_by_me;
END;
$$;

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_unread_count(
    p_user_id BIGINT,
    p_org_id  INTEGER,
    p_app_id  INTEGER
)
RETURNS TABLE (unread_count INTEGER)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT COALESCE(s.unread_by_me, 0)::INTEGER AS unread_count
    FROM public.fn_chat_announcement_get_stats(p_user_id, p_org_id, p_app_id) s;
END;
$$;




-- >>> fn_chat_announcement_mark_read.sql

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
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

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




-- >>> fn_chat_announcement_pin.sql

-- fn_chat_announcement_pin / unpin
-- OUT columns renamed to avoid plpgsql "announcement_id is ambiguous".
-- Must DROP first: CREATE OR REPLACE cannot change RETURNS TABLE column names.

DROP FUNCTION IF EXISTS public.fn_chat_announcement_unpin(BIGINT, BIGINT, INTEGER, INTEGER);
DROP FUNCTION IF EXISTS public.fn_chat_announcement_pin(BIGINT, BIGINT, INTEGER, INTEGER, BOOLEAN);
DROP FUNCTION IF EXISTS public.fn_chat_announcement_pin(BIGINT, BIGINT, INTEGER, INTEGER);

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_pin(
    p_announcement_id BIGINT,
    p_user_id         BIGINT,
    p_org_id          INTEGER,
    p_app_id          INTEGER,
    p_pinned          BOOLEAN DEFAULT TRUE
)
RETURNS TABLE (
    result_announcement_id BIGINT,
    result_is_pinned       BOOLEAN
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_posted_by BIGINT;
    v_is_pinned BOOLEAN;
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    SELECT a.posted_by
    INTO v_posted_by
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
        RAISE EXCEPTION 'Only the poster can pin/unpin this announcement' USING ERRCODE = '42501';
    END IF;

    UPDATE public.tab_announcements a
    SET is_pinned = COALESCE(p_pinned, TRUE),
        updated_at = NOW()
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
    RETURNING a.is_pinned INTO v_is_pinned;

    result_announcement_id := p_announcement_id;
    result_is_pinned := COALESCE(v_is_pinned, FALSE);
    RETURN NEXT;
END;
$$;

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_unpin(
    p_announcement_id BIGINT,
    p_user_id         BIGINT,
    p_org_id          INTEGER,
    p_app_id          INTEGER
)
RETURNS TABLE (
    result_announcement_id BIGINT,
    result_is_pinned       BOOLEAN
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT r.result_announcement_id, r.result_is_pinned
    FROM public.fn_chat_announcement_pin(
        p_announcement_id, p_user_id, p_org_id, p_app_id, FALSE
    ) AS r;
END;
$$;




-- >>> fn_chat_announcement_update.sql

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
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

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
        -- Future expiry, cleared expiry, or no expiry â†’ Active (never leave stale Expired)
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




-- >>> fn_chat_assert_group_admin.sql

-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

CREATE OR REPLACE FUNCTION public.fn_chat_assert_group_admin(p_group_id bigint, p_user_id bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS void
 LANGUAGE plpgsql
AS $function$
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_groups g
        INNER JOIN public.tab_group_members m ON m.group_id = g.group_id
        WHERE g.group_id = p_group_id
          AND g.org_id = p_org_id
          AND g.app_id = p_app_id
          AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND COALESCE(g.is_deleted, false) = false
          AND m.user_id = p_user_id::integer
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND m.left_at IS NULL
          AND COALESCE(m.is_admin, false) = true
    ) THEN
        RAISE EXCEPTION 'fn_chat_assert_group_admin: user % is not an active admin of group %',
            p_user_id, p_group_id
            USING ERRCODE = '42501';
    END IF;
END;
$function$





-- >>> fn_chat_assert_group_member.sql

-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

CREATE OR REPLACE FUNCTION public.fn_chat_assert_group_member(p_group_id bigint, p_user_id bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS void
 LANGUAGE plpgsql
AS $function$
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_groups g
        INNER JOIN public.tab_group_members m ON m.group_id = g.group_id
        WHERE g.group_id = p_group_id
          AND g.org_id = p_org_id
          AND g.app_id = p_app_id
          AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND COALESCE(g.is_deleted, false) = false
          AND m.user_id = p_user_id::integer
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND m.left_at IS NULL
    ) THEN
        RAISE EXCEPTION 'fn_chat_assert_group_member: user % is not an active member of group %',
            p_user_id, p_group_id
            USING ERRCODE = '42501';
    END IF;
END;
$function$





-- >>> fn_chat_mark_notification_read.sql

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
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'fn_chat_mark_notification_read: org_id and app_id are required'
            USING ERRCODE = '22023';
    END IF;

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




-- >>> fn_chat_user_can_access_message.sql

-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

CREATE OR REPLACE FUNCTION public.fn_chat_user_can_access_message(p_message tab_messages, p_user_id bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS boolean
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    IF p_message.sender_user_id IS NOT DISTINCT FROM p_user_id
       OR p_message.receiver_user_id IS NOT DISTINCT FROM p_user_id THEN
        RETURN true;
    END IF;

    IF p_message.group_id IS NOT NULL THEN
        RETURN EXISTS (
            SELECT 1
            FROM public.tab_group_members m
            INNER JOIN public.tab_groups g ON g.group_id = m.group_id
            WHERE m.group_id = p_message.group_id
              AND m.user_id = p_user_id::integer
              AND m.left_at IS NULL
              AND m.org_id = p_org_id
              AND m.app_id = p_app_id
              AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
              AND g.org_id = p_org_id
              AND g.app_id = p_app_id
              AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
              AND COALESCE(g.is_deleted, false) = false
        );
    END IF;

    RETURN false;
END;
$function$




