-- fn_chat_announcement_get
-- Bound to SoftOnCloud tables:
--   tab_announcements, tab_announcement_recipients, tab_announcement_category_master, tab_department_master
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
            dept.department_name,
            a.location::VARCHAR AS location,
            COALESCE(a.is_important, LOWER(TRIM(a.status_name)) = 'important') AS is_important,
            a.is_pinned,
            CASE
                WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 3
                WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 2
                WHEN LOWER(TRIM(a.status_name)) IN ('expired') THEN 3
                WHEN LOWER(TRIM(a.status_name)) IN ('expiring soon') THEN 2
                ELSE 1
            END AS status_id,
            CASE
                WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 'Expired'::VARCHAR
                WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 'Expiring Soon'::VARCHAR
                WHEN LOWER(TRIM(a.status_name)) = 'important' THEN 'Active'::VARCHAR
                ELSE a.status_name::VARCHAR
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
        LEFT JOIN public.tab_department_master dept
            ON dept.department_id = a.department_id::INTEGER
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
                      WHEN LOWER(TRIM(a.status_name)) = 'important' THEN 'active'
                      ELSE a.status_name
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
