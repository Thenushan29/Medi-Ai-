CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE TABLE patients (
        id uuid NOT NULL,
        display_name character varying(200) NOT NULL,
        status character varying(32) NOT NULL,
        status_message text,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        analyzed_at timestamp with time zone,
        CONSTRAINT pk_patients PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE TABLE alerts (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        type character varying(48) NOT NULL,
        severity character varying(16) NOT NULL,
        title character varying(300) NOT NULL,
        involved_generics text[] NOT NULL,
        explanation_en text,
        explanation_ta text,
        suggested_action_en text,
        suggested_action_ta text,
        confidence integer NOT NULL,
        requires_professional_consult boolean NOT NULL,
        verification_status character varying(24) NOT NULL,
        verification_excerpt text,
        verification_source text,
        evidence_document_ids uuid[] NOT NULL,
        detected_by text,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_alerts PRIMARY KEY (id),
        CONSTRAINT fk_alerts_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE TABLE documents (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        original_file_name character varying(500) NOT NULL,
        content_type character varying(120) NOT NULL,
        size_bytes bigint NOT NULL,
        storage_path character varying(600) NOT NULL,
        sha256 character varying(64) NOT NULL,
        visit_label text,
        status character varying(32) NOT NULL,
        failure_reason text,
        retry_count integer NOT NULL,
        raw_extraction_json jsonb,
        extraction_model text,
        prompt_tokens integer,
        completion_tokens integer,
        extraction_latency_ms integer,
        document_type text,
        document_date date,
        provider_name text,
        provider_facility text,
        overall_confidence integer,
        legibility_notes text,
        created_at timestamp with time zone NOT NULL,
        extracted_at timestamp with time zone,
        CONSTRAINT pk_documents PRIMARY KEY (id),
        CONSTRAINT fk_documents_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE TABLE allergies (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        document_id uuid NOT NULL,
        is_document_warning boolean NOT NULL,
        substance character varying(500),
        substance_generic character varying(200),
        relates_to text[] NOT NULL,
        reaction text,
        severity text,
        source_text text,
        confidence integer,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_allergies PRIMARY KEY (id),
        CONSTRAINT fk_allergies_documents_document_id FOREIGN KEY (document_id) REFERENCES documents (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE TABLE lab_results (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        document_id uuid NOT NULL,
        test_name character varying(200),
        test_name_standard character varying(200),
        value_numeric numeric(14,4),
        value_text text,
        unit text,
        normal_min numeric(14,4),
        normal_max numeric(14,4),
        normal_range_text text,
        test_date date,
        is_out_of_range boolean NOT NULL,
        source_text text,
        confidence integer,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_lab_results PRIMARY KEY (id),
        CONSTRAINT fk_lab_results_documents_document_id FOREIGN KEY (document_id) REFERENCES documents (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE TABLE medications (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        document_id uuid NOT NULL,
        brand_name character varying(200),
        generic_name character varying(200),
        strength_value numeric(12,4),
        strength_unit text,
        dose text,
        frequency text,
        frequency_per_day numeric(6,2),
        route text,
        duration_days integer,
        instructions text,
        start_date date,
        end_date date,
        source_text text,
        confidence integer,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_medications PRIMARY KEY (id),
        CONSTRAINT fk_medications_documents_document_id FOREIGN KEY (document_id) REFERENCES documents (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_alerts_patient_id_severity ON alerts (patient_id, severity);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_allergies_document_id ON allergies (document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_allergies_patient_id_is_document_warning ON allergies (patient_id, is_document_warning);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_documents_patient_id_document_date ON documents (patient_id, document_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_documents_patient_id_status ON documents (patient_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_documents_sha256 ON documents (sha256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_lab_results_document_id ON lab_results (document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_lab_results_patient_id_test_name_standard_test_date ON lab_results (patient_id, test_name_standard, test_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_medications_document_id ON medications (document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_medications_patient_id_generic_name ON medications (patient_id, generic_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    CREATE INDEX ix_patients_created_at ON patients (created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260803180058_InitialSchema') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260803180058_InitialSchema', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260817044923_AddDiagnoses') THEN
    CREATE TABLE diagnoses (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        document_id uuid NOT NULL,
        text character varying(500),
        source_text text,
        confidence integer,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_diagnoses PRIMARY KEY (id),
        CONSTRAINT fk_diagnoses_documents_document_id FOREIGN KEY (document_id) REFERENCES documents (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260817044923_AddDiagnoses') THEN
    CREATE INDEX ix_diagnoses_document_id ON diagnoses (document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260817044923_AddDiagnoses') THEN
    CREATE INDEX ix_diagnoses_patient_id ON diagnoses (patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260817044923_AddDiagnoses') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260817044923_AddDiagnoses', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260817054439_AddChatMessages') THEN
    CREATE TABLE chat_messages (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        question character varying(1000) NOT NULL,
        answer_en text NOT NULL,
        answer_ta text,
        answer_tanglish text,
        asked_language character varying(16) NOT NULL,
        citations uuid[] NOT NULL,
        confidence integer NOT NULL,
        safety_refusal boolean NOT NULL,
        consult_professional boolean NOT NULL,
        found_in_documents boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_chat_messages PRIMARY KEY (id),
        CONSTRAINT fk_chat_messages_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260817054439_AddChatMessages') THEN
    CREATE INDEX ix_chat_messages_patient_id_created_at ON chat_messages (patient_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260817054439_AddChatMessages') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260817054439_AddChatMessages', '10.0.10');
    END IF;
END $EF$;
COMMIT;

