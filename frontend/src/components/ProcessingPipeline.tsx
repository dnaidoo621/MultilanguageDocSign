"use client";

import StampSeal from "./StampSeal";

const STEPS = [
  { id: "upload", label: "UPLOAD", detail: "Receiving file · verifying" },
  { id: "ocr", label: "OCR", detail: "Surya · extracting blocks + bounding boxes" },
  { id: "segment", label: "SEGMENT", detail: "Splitting into clauses · injecting glossary" },
  { id: "translate", label: "TRANSLATE", detail: "Ollama · clause-by-clause, ko → en" },
  { id: "analyze", label: "ANALYZE", detail: "Hybrid rules + LLM risk classification" },
  { id: "ready", label: "READY", detail: "Synchronized panes · audit log opened" },
];

export default function ProcessingPipeline({
  step,
  done,
  error,
  fileName,
  clauses,
  highRisk,
  onOpen,
  onReset,
}: {
  step: number;
  done: boolean;
  error: string | null;
  fileName: string;
  clauses: number;
  highRisk: number;
  onOpen: () => void;
  onReset: () => void;
}) {
  return (
    <div className="pipeline">
      <div className="pipeline-doc">
        <div className="doc-paper">
          {!done && !error && <div className="paper-scan" />}
          <div className="paper-corner tl" />
          <div className="paper-corner tr" />
          <div className="paper-corner bl" />
          <div className="paper-corner br" />
          <div className="paper-lines">
            {Array.from({ length: 22 }).map((_, i) => (
              <div key={i} className="paper-line" style={{ width: `${42 + Math.sin(i * 1.3) * 30 + 25}%` }} />
            ))}
          </div>
          <div className="paper-label mono">{fileName}</div>
          {done && (
            <div className="paper-stamp">
              <StampSeal size={88} label="OK" sub="LINGUASIGN · PROCESSED" />
            </div>
          )}
        </div>
      </div>

      <div className="pipeline-list">
        {STEPS.map((s, i) => {
          const status = done ? "done" : i < step ? "done" : i === step ? "active" : "pending";
          const note =
            error && i === step ? "ERROR" : status === "done" ? "OK" : status === "active" ? "RUNNING…" : "—";
          return (
            <div key={s.id} className={"pipe-step " + status}>
              <div className="pipe-bullet">
                {status === "done" && <span>✓</span>}
                {status === "active" && !error && <span className="pipe-spin" />}
                {status === "active" && error && <span style={{ color: "var(--stamp)" }}>!</span>}
                {status === "pending" && <span>·</span>}
              </div>
              <div className="pipe-body">
                <div className="row between" style={{ gap: 12 }}>
                  <div className="mono" style={{ color: status === "pending" ? "var(--ink-3)" : "var(--ink)" }}>
                    [{String(i + 1).padStart(2, "0")}] {s.label}
                  </div>
                  <div className="mono-meta" style={{ color: error && i === step ? "var(--stamp)" : undefined }}>
                    {note}
                  </div>
                </div>
                <div className="pipe-detail">{s.detail}</div>
                {status === "active" && !error && (
                  <div className="pipe-progress">
                    <div />
                  </div>
                )}
              </div>
            </div>
          );
        })}
      </div>

      {(done || error) && (
        <div className="pipeline-done">
          <div>
            <div className="mono" style={{ marginBottom: 6, color: error ? "var(--stamp)" : undefined }}>
              {error ? `FAILED · ${error}` : `READY · ${clauses} CLAUSES · ${highRisk} HIGH-RISK`}
            </div>
            <p className="serif" style={{ fontSize: 26, margin: 0, letterSpacing: "-0.01em" }}>
              {error ? "Something went wrong." : "Your bilingual reader is ready."}
            </p>
          </div>
          <div className="row" style={{ gap: 10 }}>
            <button className="cta ghost" onClick={onReset}>
              Upload another
            </button>
            {!error && (
              <button className="cta stamp" data-testid="open-reader" onClick={onOpen}>
                Open reader <span className="arrow">→</span>
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
