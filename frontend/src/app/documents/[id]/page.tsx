"use client";

import dynamic from "next/dynamic";
import Link from "next/link";
import { use, useCallback, useEffect, useRef, useState } from "react";
import { apiFetch, apiJson } from "@/lib/api";
import { useUser } from "@/lib/useUser";
import type { DocumentDetail, TranslationDetail } from "@/lib/types";
import TranslationPanel from "@/components/TranslationPanel";

// react-pdf is browser-only — load without SSR.
const PdfBlockViewer = dynamic(() => import("@/components/PdfBlockViewer"), {
  ssr: false,
});

const TARGET = "en";

export default function DocumentPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { user, loading } = useUser();
  const [detail, setDetail] = useState<DocumentDetail | null>(null);
  const [translation, setTranslation] = useState<TranslationDetail | null>(null);
  const [activeBlock, setActiveBlock] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Load the document.
  useEffect(() => {
    if (!user) return;
    apiJson<DocumentDetail>(`/api/documents/${id}`)
      .then(setDetail)
      .catch((e) => setError(e instanceof Error ? e.message : "Failed to load."));
  }, [id, user]);

  // Load any existing translation (404 just means none yet).
  useEffect(() => {
    if (!user) return;
    apiJson<TranslationDetail>(`/api/documents/${id}/translation?target=${TARGET}`)
      .then(setTranslation)
      .catch(() => {});
  }, [id, user]);

  // Poll while a translation is in progress.
  const status = translation?.status;
  useEffect(() => {
    if (status !== "Pending" && status !== "Translating") return;
    pollRef.current = setInterval(async () => {
      try {
        setTranslation(
          await apiJson<TranslationDetail>(`/api/documents/${id}/translation?target=${TARGET}`),
        );
      } catch {
        /* ignore transient errors */
      }
    }, 2000);
    return () => {
      if (pollRef.current) clearInterval(pollRef.current);
    };
  }, [status, id]);

  const translate = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      await apiFetch(`/api/documents/${id}/translate`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ targetLanguage: TARGET }),
      });
      setTranslation(
        await apiJson<TranslationDetail>(`/api/documents/${id}/translation?target=${TARGET}`),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "Translation failed to start.");
    } finally {
      setBusy(false);
    }
  }, [id]);

  if (loading) return <main className="p-8 text-sm text-gray-500">Loading…</main>;
  if (!user)
    return (
      <main className="p-8 text-sm">
        Please <Link href="/dashboard" className="underline">sign in</Link>.
      </main>
    );

  const isTranslating = status === "Pending" || status === "Translating";
  const isDone = status === "Completed";

  return (
    <main className="mx-auto flex max-w-7xl flex-col gap-4 p-6">
      <Link href="/dashboard" className="text-sm text-blue-600 underline">
        ← Back
      </Link>

      {error && <p className="text-sm text-red-600">{error}</p>}
      {!detail && !error && <p className="text-sm text-gray-500">Loading document…</p>}

      {detail && (
        <>
          <header className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h1 className="text-xl font-semibold">{detail.fileName}</h1>
              <p className="text-sm text-gray-500">
                {detail.pageCount} pages
                {detail.sourceLanguage ? ` · detected: ${detail.sourceLanguage}` : ""}
              </p>
            </div>
            <div className="flex items-center gap-3">
              {isTranslating && (
                <span className="text-sm text-gray-500" data-testid="translation-status">
                  Translating…
                </span>
              )}
              {status === "Failed" && (
                <span className="text-sm text-red-600">Translation failed</span>
              )}
              <button
                data-testid="translate-button"
                onClick={translate}
                disabled={busy || isTranslating}
                className="rounded-full bg-black px-4 py-2 text-sm font-medium text-white hover:bg-gray-800 disabled:opacity-50"
              >
                {isDone ? "Re-translate" : "Translate to English"}
              </button>
            </div>
          </header>

          <p className="text-xs text-gray-400">
            Hover a block or a translated clause to highlight its counterpart. AI translation —
            not certified or legal advice.
          </p>

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <div className="overflow-auto">
              <PdfBlockViewer
                detail={detail}
                pageWidth={520}
                activeBlock={activeBlock}
                onHover={setActiveBlock}
              />
            </div>
            <div className="overflow-auto" data-testid="translation-pane">
              {isDone && translation ? (
                <TranslationPanel
                  translation={translation}
                  activeBlock={activeBlock}
                  onHover={setActiveBlock}
                />
              ) : (
                <p className="text-sm text-gray-400">
                  {isTranslating
                    ? "Translating the document clause by clause…"
                    : "Click “Translate to English” to generate a synchronized translation."}
                </p>
              )}
            </div>
          </div>
        </>
      )}
    </main>
  );
}
