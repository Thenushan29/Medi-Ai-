-- MediTrail — derived views.
-- Run after 01_schema.sql. Safe to re-run.
--
-- PRD §12.3: "v_patient_timeline is a view, not a table: the timeline is derived, never stored twice."
-- Storing derived data twice creates synchronization defects (§28.1), so the timeline is computed
-- on read from documents + its child rows.

CREATE OR REPLACE VIEW v_patient_timeline AS
SELECT
    d.id                AS document_id,
    d.patient_id,
    d.document_date,
    d.visit_label,
    d.document_type,
    d.provider_name,
    d.provider_facility,
    d.original_file_name,
    d.storage_path,
    d.status,
    d.overall_confidence,
    d.legibility_notes,
    d.failure_reason,
    d.created_at,
    COALESCE(m.medication_count, 0)  AS medication_count,
    COALESCE(l.lab_result_count, 0)  AS lab_result_count,
    COALESCE(a.allergy_count, 0)     AS allergy_count,
    COALESCE(a.warning_count, 0)     AS warning_count,
    COALESCE(l.out_of_range_count, 0) AS out_of_range_count
FROM documents d
LEFT JOIN (
    SELECT document_id, COUNT(*) AS medication_count
    FROM medications GROUP BY document_id
) m ON m.document_id = d.id
LEFT JOIN (
    SELECT document_id,
           COUNT(*) AS lab_result_count,
           COUNT(*) FILTER (WHERE is_out_of_range) AS out_of_range_count
    FROM lab_results GROUP BY document_id
) l ON l.document_id = d.id
LEFT JOIN (
    SELECT document_id,
           COUNT(*) FILTER (WHERE NOT is_document_warning) AS allergy_count,
           COUNT(*) FILTER (WHERE is_document_warning)     AS warning_count
    FROM allergies GROUP BY document_id
) a ON a.document_id = d.id;

-- Documents with no readable date sort last rather than vanishing — an undated document is still
-- evidence and must stay visible in the timeline (§9.3 failure flows).
COMMENT ON VIEW v_patient_timeline IS
    'Derived chronological view of a patient''s documents with per-document extraction counts. Order by (document_date NULLS LAST, created_at).';
