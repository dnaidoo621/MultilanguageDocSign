"use client";

import dynamic from "next/dynamic";
import Link from "next/link";
import { use, useEffect, useState } from "react";
import { apiJson } from "@/lib/api";
import { useUser } from "@/lib/useUser";
import type { DocumentDetail } from "@/lib/types";

// react-pdf is browser-only — load without SSR.
const PdfBlockViewer = dynamic(() => import("@/components/PdfBlockViewer"), {
  ssr: false,
});

export default function DocumentPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { user, loading } = useUser();
  const [detail, setDetail] = useState<DocumentDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!user) return;
    apiJson<DocumentDetail>(`/api/documents/${id}`)
      .then(setDetail)
      .catch((e) => setError(e instanceof Error ? e.message : "Failed to load."));
  }, [id, user]);

  if (loading) return <main className="p-8 text-sm text-gray-500">Loading…</main>;
  if (!user)
    return (
      <main className="p-8 text-sm">
        Please <Link href="/dashboard" className="underline">sign in</Link>.
      </main>
    );

  return (
    <main className="mx-auto flex max-w-3xl flex-col gap-4 p-8">
      <Link href="/dashboard" className="text-sm text-blue-600 underline">
        ← Back
      </Link>

      {error && <p className="text-sm text-red-600">{error}</p>}
      {!detail && !error && <p className="text-sm text-gray-500">Loading document…</p>}

      {detail && (
        <>
          <header>
            <h1 className="text-xl font-semibold">{detail.fileName}</h1>
            <p className="text-sm text-gray-500">
              {detail.pageCount} pages
              {detail.sourceLanguage ? ` · detected: ${detail.sourceLanguage}` : ""}
            </p>
          </header>
          <p className="text-xs text-gray-400">
            Hover a highlighted block to see its extracted text. Translation arrives in Phase 2.
          </p>
          <PdfBlockViewer detail={detail} />
        </>
      )}
    </main>
  );
}
