-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.
-- profile_pic: varchar(255) on tab_groups (path or URL).

-- DROP old signature (no profile_pic) so REPLACE does not leave an overload.
DROP FUNCTION IF EXISTS public.fn_chat_update_group(bigint, character varying, character varying, bigint, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_update_group(
    p_group_id bigint,
    p_group_name character varying,
    p_description character varying,
    p_requesting_user_id bigint,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer,
    p_profile_pic character varying DEFAULT NULL
)
 RETURNS TABLE(
    group_id bigint,
    chat_id integer,
    group_name character varying,
    group_code character varying,
    description character varying,
    created_by integer,
    created_at timestamp with time zone,
    is_active boolean,
    profile_pic character varying
)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_name varchar(150);
    v_desc varchar(255);
    v_pic varchar(255);
BEGIN
    IF p_group_id IS NULL OR p_group_id <= 0
       OR p_requesting_user_id IS NULL OR p_requesting_user_id <= 0
       OR p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_update_group: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    PERFORM public.fn_chat_assert_group_admin(
        p_group_id, p_requesting_user_id, p_org_id, p_app_id, p_fiscal_year_id);

    v_name := NULLIF(btrim(COALESCE(p_group_name, '')), '');
    v_desc := NULLIF(btrim(COALESCE(p_description, '')), '');
    -- NULL = leave existing profile_pic unchanged; '' = clear; otherwise set.
    v_pic := CASE
        WHEN p_profile_pic IS NULL THEN NULL
        ELSE NULLIF(btrim(p_profile_pic), '')
    END;

    IF v_name IS NULL THEN
        RAISE EXCEPTION 'fn_chat_update_group: group_name is required'
            USING ERRCODE = '22023';
    END IF;

    UPDATE public.tab_groups g
    SET group_name = v_name,
        description = v_desc,
        profile_pic = CASE
            WHEN p_profile_pic IS NULL THEN g.profile_pic
            ELSE v_pic
        END,
        updated_at = CURRENT_TIMESTAMP
    WHERE g.group_id = p_group_id
      AND g.org_id = p_org_id
      AND g.app_id = p_app_id
      AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(g.is_deleted, false) = false;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'fn_chat_update_group: group % not found or inactive', p_group_id
            USING ERRCODE = 'P0002';
    END IF;

    RETURN QUERY
    SELECT
        g.group_id,
        g.chat_id,
        g.group_name,
        g.group_code,
        g.description,
        g.created_by,
        g.created_on,
        (COALESCE(g.is_deleted, false) = false),
        g.profile_pic
    FROM public.tab_groups g
    WHERE g.group_id = p_group_id;
END;
$function$
