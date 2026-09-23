-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

CREATE OR REPLACE FUNCTION public.fn_chat_create_group(p_group_name character varying, p_group_code character varying, p_description character varying, p_created_by bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer, p_member_user_ids bigint[] DEFAULT NULL::bigint[])
 RETURNS TABLE(group_id bigint, chat_id integer, group_name character varying, group_code character varying, description character varying, created_by integer, created_at timestamp with time zone, is_active boolean, is_admin boolean)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_group_id bigint;
    v_chat_id integer;
    v_name varchar(150);
    v_code varchar(50);
    v_desc varchar(255);
    v_created_on timestamptz;
    v_member_id bigint;
BEGIN
    IF p_created_by IS NULL OR p_created_by <= 0
       OR p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_create_group: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    v_name := NULLIF(btrim(COALESCE(p_group_name, '')), '');
    v_code := NULLIF(btrim(COALESCE(p_group_code, '')), '');
    v_desc := NULLIF(btrim(COALESCE(p_description, '')), '');

    IF v_name IS NULL THEN
        RAISE EXCEPTION 'fn_chat_create_group: group_name is required'
            USING ERRCODE = '22023';
    END IF;

    IF v_code IS NULL THEN
        RAISE EXCEPTION 'fn_chat_create_group: group_code is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_created_by > 2147483647 THEN
        RAISE EXCEPTION 'fn_chat_create_group: created_by exceeds integer range'
            USING ERRCODE = '22023';
    END IF;

    -- Allocate a chat_id in the same space as private chats (tab_messages.chat_id).
    SELECT COALESCE(MAX(m.chat_id), 0) + 1
    INTO v_chat_id
    FROM public.tab_messages m;

    -- Also avoid colliding with existing group chat_ids that have no messages yet.
    SELECT GREATEST(v_chat_id, COALESCE(MAX(g.chat_id), 0) + 1)
    INTO v_chat_id
    FROM public.tab_groups g;

    INSERT INTO public.tab_groups (
        org_id,
        app_id,
        group_name,
        group_code,
        description,
        created_by,
        created_on,
        updated_at,
        is_deleted,
        fiscal_year_id,
        chat_id
    )
    VALUES (
        p_org_id,
        p_app_id,
        v_name,
        v_code,
        v_desc,
        p_created_by::integer,
        CURRENT_TIMESTAMP,
        CURRENT_TIMESTAMP,
        false,
        p_fiscal_year_id,
        v_chat_id
    )
    RETURNING public.tab_groups.group_id, public.tab_groups.created_on
    INTO v_group_id, v_created_on;

    -- Creator always becomes active admin member.
    INSERT INTO public.tab_group_members (
        group_id,
        user_id,
        app_id,
        joined_on,
        left_at,
        is_admin,
        org_id,
        fiscal_year_id
    )
    VALUES (
        v_group_id,
        p_created_by::integer,
        p_app_id,
        v_created_on,
        NULL,
        true,
        p_org_id,
        p_fiscal_year_id
    );

    -- Optional additional SoftOnCloud userIds (non-admin). Skip creator duplicates.
    IF p_member_user_ids IS NOT NULL THEN
        FOREACH v_member_id IN ARRAY p_member_user_ids
        LOOP
            IF v_member_id IS NULL OR v_member_id <= 0 OR v_member_id = p_created_by THEN
                CONTINUE;
            END IF;

            IF v_member_id > 2147483647 THEN
                RAISE EXCEPTION 'fn_chat_create_group: member user_id % exceeds integer range', v_member_id
                    USING ERRCODE = '22023';
            END IF;

            INSERT INTO public.tab_group_members (
                group_id,
                user_id,
                app_id,
                joined_on,
                left_at,
                is_admin,
                org_id,
                fiscal_year_id
            )
            VALUES (
                v_group_id,
                v_member_id::integer,
                p_app_id,
                CURRENT_TIMESTAMP,
                NULL,
                false,
                p_org_id,
                p_fiscal_year_id
            )
            -- Qualify via constraint name: RETURNS TABLE(group_id ...) makes
            -- ON CONFLICT (group_id, user_id) ambiguous with the OUT parameter.
            ON CONFLICT ON CONSTRAINT tab_group_members_group_id_user_id_key DO UPDATE
            SET left_at = NULL,
                is_admin = false,
                joined_on = CURRENT_TIMESTAMP,
                app_id = EXCLUDED.app_id,
                org_id = EXCLUDED.org_id,
                fiscal_year_id = EXCLUDED.fiscal_year_id;
        END LOOP;
    END IF;

    group_id := v_group_id;
    chat_id := v_chat_id;
    group_name := v_name;
    group_code := v_code;
    description := v_desc;
    created_by := p_created_by::integer;
    created_at := v_created_on;
    is_active := true;
    is_admin := true;
    RETURN NEXT;
END;
$function$

