"use client";

import dynamic from "next/dynamic";
import Link from "next/link";
import { use, useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { apiFetch, apiJson } from "@/lib/api";
import { useUser } from "@/lib/useUser";
import type { AnalysisDetail, DocumentDetail, TranslationDetail } from "@/lib/types";
import { BilingualReader, type ReaderClause } from "@/components/BilingualReader";
import { toRiskLevel } from "@/components/RiskDot";
import LangChip from "@/components/LangChip";
import SignAndExport from "@/components/SignAndExport";

const PdfBlockViewer = dynamic(() => import("@/components/PdfBlockViewer"), { ssr: false });

type Layout = "mirrored" | "margin" | "pdf";

export default function DocumentPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { user, loading } = useUser();

  const [detail, setDetail] = useState<DocumentDetail | null>(null);
  const [translation, setTranslation] = useState<TranslationDetail | null>(null);
  const [analysis, setAnalysis] = useState<AnalysisDetail | null>(null);
  const [layout, setLayout] = useState<Layout>("mirrored");
  const [riskFilter, setRiskFilter] = useState<"all" | "med" | "high">("all");
  const [active, setActive] = useState<string | null>(null);
  const [pinned, setPinned] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!user) return;
    apiJson<DocumentDetail>(`/api/documents/${id}`).then(setDetail).catch(() => {});
    apiJson<TranslationDetail>(`/api/documents/${id}/translation?target=en`).then(setTranslation).catch(() => {});
    apiJson<AnalysisDetail>(`/api/documents/${id}/analysis`).then(setAnalysis).catch(() => {});
  }, [id, user]);

  const tStatus = translation?.status;
  useEffect(() => {
    if (tStatus !== "Pending" && tStatus !== "Translating") return;
    const t = setInterval(async () => {
      try {
        setTranslation(await apiJson<TranslationDetail>(`/api/documents/${id}/translation?target=en`));
      } catch {}
    }, 2000);
    return () => clearInterval(t);
  }, [tStatus, id]);

  const aStatus = analysis?.status;
  useEffect(() => {
    if (aStatus !== "Pending" && aStatus !== "Analyzing") return;
    const t = setInterval(async () => {
      try {
        setAnalysis(await apiJson<AnalysisDetail>(`/api/documents/${id}/analysis`));
      } catch {}
    }, 2500);
    return () => clearInterval(t);
  }, [aStatus, id]);

  const prepare = useCallback(async () => {
    setBusy(true);
    try {
      await apiFetch(`/api/documents/${id}/translate`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ targetLanguage: "en" }),
      });
      setTranslation(await apiJson<TranslationDetail>(`/api/documents/${id}/translation?target=en`));
      await apiFetch(`/api/documents/${id}/analyze`, { method: "POST" });
      setAnalysis(await apiJson<AnalysisDetail>(`/api/documents/${id}/analysis`));
    } catch {
      /* ignore */
    } finally {
      setBusy(false);
    }
  }, [id]);

  const clauses: ReaderClause[] = useMemo(() => {
    const fmap = new Map((analysis?.findings ?? []).map((f) => [f.sourceBlockId, f]));
    return (translation?.segments ?? []).map((s, i) => {
      const f = fmap.get(s.sourceBlockId);
      return {
        id: s.sourceBlockId,
        ref: `§${i + 1}`,
        page: s.pageNumber,
        risk: toRiskLevel(f?.riskLevel),
        riskType: f?.riskType ?? "",
        explain: f?.explanation ?? "",
        src: s.sourceText,
        en: s.translatedText,
      };
    });
  }, [translation, analysis]);

  const riskByBlock = useMemo(() => {
    const m: Record<string, string> = {};
    for (const f of analysis?.findings ?? []) if (f.riskLevel !== "None") m[f.sourceBlockId] = f.riskLevel;
    return m;
  }, [analysis]);

  const highs = clauses.filter((c) => c.risk === "high");
  const meds = clauses.filter((c) => c.risk === "med");
  const visible = clauses.filter((c) =>
    riskFilter === "high" ? c.risk === "high" : riskFilter === "med" ? c.risk === "med" || c.risk === "high" : true,
  );

  if (loading) return <main className="screen mono-meta">Loading…</main>;
  if (!user)
    return (
      <main className="screen">
        Please{" "}
        <Link href="/dashboard" className="underline">
          sign in
        </Link>
        .
      </main>
    );
  if (!detail) return <main className="screen mono-meta">Loading document…</main>;

  const translationReady = tStatus === "Completed";
  const jump = (cid: string) => {
    setPinned(cid);
    setActive(cid);
  };

  return (
    <main className="reader">
      <header className="reader-head">
        <div className="wrap">
          <div className="section-eyebrow solo">
            <span>DOC · {detail.id.slice(0, 8)}</span>
            <span style={{ flex: 1, height: 1, background: "var(--rule)" }} />
            <span>{detail.pageCount} PP</span>
          </div>
          <div className="reader-head-grid">
            <div>
              <h1 className="reader-title">{detail.fileName}</h1>
              <p className="reader-sub serif">
                {detail.sourceLanguage ? `${detail.sourceLanguage} → English` : "bilingual review"}
              </p>
            </div>
            <div className="reader-head-meta">
              <MetaRow k="FILE" v={detail.fileName} />
              <MetaRow k="LANG" v={detail.sourceLanguage ? <LangChip from={detail.sourceLanguage} to="en" /> : "—"} />
              <MetaRow k="PAGES" v={`${detail.pageCount} pp · ${clauses.length} clauses`} />
              <MetaRow k="STATUS" v={detail.status} />
            </div>
          </div>
        </div>
      </header>

      <div className="reader-controls">
        <div className="wrap row between" style={{ flexWrap: "wrap", gap: 12 }}>
          <div className="row" style={{ gap: 6, flexWrap: "wrap" }}>
            <div className="seg">
              {(["mirrored", "margin", "pdf"] as const).map((v) => (
                <button key={v} className={layout === v ? "active" : ""} onClick={() => setLayout(v)}>
                  {v}
                </button>
              ))}
            </div>
            <span className="control-label">LAYOUT</span>
          </div>
          <div className="row" style={{ gap: 6, flexWrap: "wrap" }}>
            <span className="control-label">RISK</span>
            <button className={"filter-tab " + (riskFilter === "all" ? "active" : "")} onClick={() => setRiskFilter("all")}>
              All<span className="tab-count">{clauses.length}</span>
            </button>
            <button className={"filter-tab " + (riskFilter === "med" ? "active" : "")} onClick={() => setRiskFilter("med")}>
              Med+<span className="tab-count">{meds.length + highs.length}</span>
            </button>
            <button
              className={"filter-tab " + (riskFilter === "high" ? "active accent" : "")}
              onClick={() => setRiskFilter("high")}
            >
              High only<span className="tab-count">{highs.length}</span>
            </button>
          </div>
        </div>
      </div>

      {!translationReady ? (
        <div className="wrap" style={{ padding: 24 }}>
          <div className="risk-summary">
            <p className="serif" style={{ fontSize: 20, margin: "0 0 12px" }}>
              {tStatus === "Pending" || tStatus === "Translating"
                ? "Preparing your bilingual reader…"
                : "This document hasn't been translated yet."}
            </p>
            <button
              data-testid="translate-button"
              className="cta stamp"
              disabled={busy || tStatus === "Translating" || tStatus === "Pending"}
              onClick={prepare}
            >
              Generate bilingual reader <span className="arrow">→</span>
            </button>
          </div>
        </div>
      ) : (
        <>
          {analysis?.status === "Completed" && (
            <div className="wrap" style={{ padding: "20px 24px 0" }}>
              <div className="risk-summary" data-testid="risk-summary">
                <div className="risk-summary-head">
                  <div className="mono">RISK SUMMARY</div>
                  <div className="mono-meta">
                    {highs.length} high · {meds.length} medium
                  </div>
                </div>
                {highs.length === 0 ? (
                  <p style={{ margin: 0, color: "var(--ink-3)" }}>No high-risk clauses detected.</p>
                ) : (
                  <div className="risk-summary-list">
                    {highs.map((c) => (
                      <button key={c.id} className="risk-item" onClick={() => jump(c.id)}>
                        <span className="risk-pill h">{c.riskType?.toUpperCase()}</span>
                        <span className="risk-ref mono-meta">{c.ref}</span>
                        <span className="risk-explain">{c.explain}</span>
                        <span className="risk-arrow mono">JUMP →</span>
                      </button>
                    ))}
                  </div>
                )}
              </div>
            </div>
          )}

          <div className="wrap" style={{ padding: "20px 24px var(--gap-5)" }}>
            {layout === "pdf" ? (
              <PdfBlockViewer
                detail={detail}
                pageWidth={760}
                activeBlock={active}
                onHover={setActive}
                riskByBlock={riskByBlock}
              />
            ) : (
              <BilingualReader
                clauses={visible}
                layout={layout}
                riskOverlay="badges"
                active={active}
                pinned={pinned}
                onActive={setActive}
                onTogglePin={(cid) => setPinned((p) => (p === cid ? null : cid))}
              />
            )}
          </div>
        </>
      )}

      <div className="reader-cta">
        <div className="wrap row between" style={{ flexWrap: "wrap", gap: 14 }}>
          <div className="mono-meta">
            {clauses.length} clauses · {highs.length} high-risk
          </div>
          <a className="cta stamp" href="#sign">
            Continue to sign <span className="arrow">→</span>
          </a>
        </div>
      </div>

      <div className="wrap" id="sign" style={{ padding: "28px 24px var(--gap-5)" }}>
        <SignAndExport documentId={detail.id} fileName={detail.fileName} />
      </div>
    </main>
  );
}

function MetaRow({ k, v }: { k: string; v: ReactNode }) {
  return (
    <div className="meta-row">
      <div className="mono">{k}</div>
      <div className="meta-val">{v}</div>
    </div>
  );
}
