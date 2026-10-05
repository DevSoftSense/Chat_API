-- ============================================================
-- SOLUTION: LIVE Chat SoftOnCloud errors FIX
-- Run ONCE on LIVE tenant DB in pgAdmin (F5)
-- Fixes:
--   1) column g.chat_id does not exist      (Messages)
--   2) column msg.group_id does not exist   (Groups)
--   3) column a.location does not exist     (Announcements)
--   4) tab_role_master does not exist       (Settings)
-- Then updates SoftOnCloud announcement functions
-- ============================================================

-- ========== PART A: SCHEMA ==========

ALTER TABLE public.tab_groups
  ADD COLUMN IF NOT EXISTS chat_id integer NULL;

CREATE INDEX IF NOT EXISTS ix_tab_groups_chat_id
  ON public.tab_groups (chat_id)
  WHERE chat_id IS NOT NULL;

UPDATE public.tab_groups g
SET chat_id = sub.chat_id
FROM (
  SELECT DISTINCT ON (m.group_id) m.group_id, m.chat_id
  FROM public.tab_messages m
  WHERE m.group_id IS NOT NULL
  ORDER BY m.group_id, m.message_id
) sub
WHERE g.group_id = sub.group_id AND g.chat_id IS NULL;

UPDATE public.tab_groups g
SET chat_id = g.group_id::integer
WHERE g.chat_id IS NULL
  AND g.group_id IS NOT NULL
  AND g.group_id <= 2147483647;

ALTER TABLE public.tab_messages
  ADD COLUMN IF NOT EXISTS group_id bigint NULL;

CREATE INDEX IF NOT EXISTS ix_tab_messages_group_id
  ON public.tab_messages (group_id)
  WHERE group_id IS NOT NULL;

UPDATE public.tab_messages m
SET group_id = g.group_id
FROM public.tab_groups g
WHERE m.chat_id = g.chat_id
  AND m.group_id IS NULL
  AND COALESCE(g.is_deleted, false) = false;

ALTER TABLE public.tab_announcements
  ADD COLUMN IF NOT EXISTS location varchar(255) NULL;

ALTER TABLE public.tab_announcements
  ADD COLUMN IF NOT EXISTS is_important boolean NOT NULL DEFAULT false;

ALTER TABLE public.tab_announcements
  ADD COLUMN IF NOT EXISTS is_pinned boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS public.tab_role_master (
    role_id integer NOT NULL, org_id integer NOT NULL, app_id integer NOT NULL,
    role_name varchar(255) NULL, is_active boolean DEFAULT true,
    is_deleted boolean DEFAULT false,
    PRIMARY KEY (role_id, org_id, app_id)
);

CREATE TABLE IF NOT EXISTS public.tab_user_role (
    user_id integer NOT NULL, role_id integer NOT NULL,
    org_id integer NOT NULL, app_id integer NOT NULL,
    is_active integer DEFAULT 1,
    PRIMARY KEY (user_id, role_id, org_id, app_id)
);

-- ========== PART B: ANNOUNCEMENT FUNCTIONS (SoftOnCloud) ==========

-- >>> from fn_chat_announcement_get_by_id.sql
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
        NULL::VARCHAR AS department_name,
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
    WHERE a.announcement_id = p_announcement_id
      AND a.org_id = p_org_id
      AND a.app_id = p_app_id
      AND COALESCE(a.delete_flag, 0) = 0;
END;
$$;



-- >>> from fn_chat_announcement_get.sql
-- fn_chat_announcement_get
-- Bound to Chat tables:
--   tab_announcements, tab_announcement_recipients, tab_announcement_category_master
-- SoftOnCloud owns department master â€” no tab_department_master join.
-- Signature matches AnnouncementRepository.GetAnnouncementsAsync.

CREATE OR REPLACE FUNCTION public.fn_chat_announcement_get(
    p_user_id        BIGINT,
    p_org_id         INTEGER,
    p_app_id         INTEGER,
    p_fiscal_year_id INTEGER DEFAULT NULL,
    p_page           INTEGER DEFAULT 1,
    p_page_size      INTEGER DEFAULT 10,
    p_search         VARCHAR DEFAULT NULL,
    p_category_id    INTEGER DEFAULT NULL,
    p_department_id  INTEGER DEFAULT NULL,
    p_location       VARCHAR DEFAULT NULL,
    p_important      BOOLEAN DEFAULT NULL,
    p_pinned         BOOLEAN DEFAULT NULL,
    p_status_id      INTEGER DEFAULT NULL,
    p_filter         VARCHAR DEFAULT NULL, -- all | organization | department | location | important
    p_sort_by        VARCHAR DEFAULT 'newest' -- newest | oldest | important | most_read
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
    total_count           BIGINT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_page      INTEGER := GREATEST(COALESCE(p_page, 1), 1);
    v_page_size INTEGER := LEAST(GREATEST(COALESCE(p_page_size, 10), 1), 100);
    v_offset    INTEGER;
    v_filter    TEXT := LOWER(TRIM(COALESCE(p_filter, 'all')));
    v_sort      TEXT := LOWER(TRIM(COALESCE(p_sort_by, 'newest')));
    v_search    TEXT := NULLIF(TRIM(COALESCE(p_search, '')), '');
    v_status    TEXT;
BEGIN
    IF p_user_id IS NULL OR p_user_id <= 0 THEN
        RAISE EXCEPTION 'userId is required' USING ERRCODE = '22023';
    END IF;
    IF p_org_id IS NULL OR p_org_id <= 0 OR p_app_id IS NULL OR p_app_id <= 0 THEN
        RAISE EXCEPTION 'orgId and appId are required' USING ERRCODE = '22023';
    END IF;

    v_offset := (v_page - 1) * v_page_size;
    v_status := CASE p_status_id
        WHEN 1 THEN 'active'
        WHEN 2 THEN 'expiring soon'
        WHEN 3 THEN 'expired'
        ELSE NULL
    END;

    RETURN QUERY
    WITH base AS (
        SELECT
            a.announcement_id,
            a.title,
            a.description,
            a.category_id::INTEGER AS category_id,
            cat.category_name,
            a.department_id::INTEGER AS department_id,
            NULL::VARCHAR AS department_name,
            a.location::VARCHAR AS location,
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
            ), FALSE) AS is_read_by_current_user
        FROM public.tab_announcements a
        LEFT JOIN public.tab_announcement_category_master cat
            ON cat.category_id = a.category_id
        WHERE a.org_id = p_org_id
          AND a.app_id = p_app_id
          AND COALESCE(a.delete_flag, 0) = 0
          AND LOWER(TRIM(a.status_name)) <> 'deleted'
          AND (p_fiscal_year_id IS NULL OR a.fiscal_year_id IS NULL OR a.fiscal_year_id = p_fiscal_year_id)
          AND (
              a.posted_by = p_user_id
              OR EXISTS (
                  SELECT 1
                  FROM public.tab_announcement_recipients r
                  WHERE r.announcement_id = a.announcement_id
                    AND r.user_id::BIGINT = p_user_id
              )
          )
          AND (p_category_id IS NULL OR a.category_id = p_category_id::SMALLINT)
          AND (p_department_id IS NULL OR a.department_id = p_department_id::BIGINT)
          AND (
              p_location IS NULL
              OR NULLIF(TRIM(COALESCE(p_location, '')), '') IS NULL
              OR LOWER(TRIM(COALESCE(a.location, ''))) = LOWER(TRIM(p_location))
          )
          AND (
              p_important IS NULL
              OR COALESCE(a.is_important, LOWER(TRIM(a.status_name)) = 'important') = p_important
          )
          AND (p_pinned IS NULL OR a.is_pinned = p_pinned)
          AND (
              v_status IS NULL
              OR LOWER(TRIM(
                  CASE
                      WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 'expired'
                      WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 'expiring soon'
                      ELSE 'active'
                  END
              )) = v_status
          )
          AND (
              v_filter = 'all'
              OR (
                  v_filter = 'important'
                  AND COALESCE(a.is_important, LOWER(TRIM(a.status_name)) = 'important')
              )
              OR (
                  -- Department: explicit department_id OR category named like "*Department*"
                  v_filter = 'department'
                  AND (
                      a.department_id IS NOT NULL
                      OR COALESCE(cat.category_name, '') ILIKE '%department%'
                  )
              )
              OR (
                  v_filter = 'location'
                  AND NULLIF(TRIM(COALESCE(a.location, '')), '') IS NOT NULL
              )
              OR (
                  -- Organization-wide: no department, no location, not a department category
                  v_filter = 'organization'
                  AND a.department_id IS NULL
                  AND NULLIF(TRIM(COALESCE(a.location, '')), '') IS NULL
                  AND COALESCE(cat.category_name, '') NOT ILIKE '%department%'
              )
          )
          AND (
              v_search IS NULL
              OR a.title ILIKE '%' || v_search || '%'
              OR a.description ILIKE '%' || v_search || '%'
          )
    ),
    counted AS (
        SELECT b.*, COUNT(*) OVER() AS total_count
        FROM base b
    )
    SELECT
        c.announcement_id,
        c.title::VARCHAR,
        c.description,
        c.category_id,
        c.category_name::VARCHAR,
        c.department_id,
        c.department_name::VARCHAR,
        c.location,
        c.is_important,
        c.is_pinned,
        c.status_id,
        c.status_name::VARCHAR,
        c.posted_by,
        c.posted_by_name,
        c.posted_date,
        c.expiry_date,
        c.read_count,
        c.recipient_count,
        c.is_read_by_current_user,
        c.total_count
    FROM counted c
    ORDER BY
        c.is_pinned DESC,
        CASE WHEN v_sort = 'newest' THEN EXTRACT(EPOCH FROM c.posted_date) END DESC NULLS LAST,
        CASE WHEN v_sort = 'oldest' THEN EXTRACT(EPOCH FROM c.posted_date) END ASC NULLS LAST,
        CASE WHEN v_sort = 'important' THEN CASE WHEN c.is_important THEN 1 ELSE 0 END END DESC,
        CASE WHEN v_sort = 'most_read' THEN c.read_count END DESC NULLS LAST,
        c.posted_date DESC
    LIMIT v_page_size OFFSET v_offset;
END;
$$;



-- >>> from fn_chat_announcement_create.sql
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

    -- SoftOnCloud owns HR department masters. Chat stores optional department_id only;
    -- display name comes from SoftOnCloud profile / product-users (p_department_name echo).
    -- Do not look up tab_department_master.

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



-- >>> from fn_chat_announcement_update.sql
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

    -- SoftOnCloud owns HR department masters â€” do not look up tab_department_master.

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



-- ========== VERIFY ==========
SELECT 'tab_groups.chat_id' AS item, EXISTS (
  SELECT 1 FROM information_schema.columns
  WHERE table_schema='public' AND table_name='tab_groups' AND column_name='chat_id'
) AS ok
UNION ALL SELECT 'tab_messages.group_id', EXISTS (
  SELECT 1 FROM information_schema.columns
  WHERE table_schema='public' AND table_name='tab_messages' AND column_name='group_id'
)
UNION ALL SELECT 'tab_announcements.location', EXISTS (
  SELECT 1 FROM information_schema.columns
  WHERE table_schema='public' AND table_name='tab_announcements' AND column_name='location'
)
UNION ALL SELECT 'tab_role_master', EXISTS (
  SELECT 1 FROM information_schema.tables
  WHERE table_schema='public' AND table_name='tab_role_master'
)
UNION ALL SELECT 'fn_chat_announcement_get', EXISTS (
  SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
  WHERE n.nspname='public' AND p.proname='fn_chat_announcement_get'
);