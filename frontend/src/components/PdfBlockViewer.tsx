"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Document, Page, pdfjs } from "react-pdf";
import "react-pdf/dist/Page/AnnotationLayer.css";
import "react-pdf/dist/Page/TextLayer.css";
import { apiFetch } from "@/lib/api";
import type { DocumentDetail, PageDto } from "@/lib/types";

// Load the pdf.js worker from a CDN matching the bundled version.
pdfjs.GlobalWorkerOptions.workerSrc = `https://unpkg.com/pdfjs-dist@${pdfjs.version}/build/pdf.worker.min.mjs`;

export default function PdfBlockViewer({
  detail,
  pageWidth = 700,
  activeBlock,
  onHover,
}: {
  detail: DocumentDetail;
  pageWidth?: number;
  activeBlock: string | null;
  onHover: (id: string | null) => void;
}) {
  const [fileUrl, setFileUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let revoked: string | null = null;
    (async () => {
      try {
        const res = await apiFetch(`/api/documents/${detail.id}/file`);
        const blob = await res.blob();
        const url = URL.createObjectURL(blob);
        revoked = url;
        setFileUrl(url);
      } catch (e) {
        setError(e instanceof Error ? e.message : "Failed to load PDF.");
      }
    })();
    return () => {
      if (revoked) URL.revokeObjectURL(revoked);
    };
  }, [detail.id]);

  const file = useMemo(() => (fileUrl ? { url: fileUrl } : null), [fileUrl]);

  if (error) return <p className="text-sm text-red-600">{error}</p>;
  if (!file) return <p className="text-sm text-gray-500">Loading document…</p>;

  return (
    <Document file={file} loading={<p className="text-sm text-gray-500">Loading PDF…</p>}>
      {detail.pages.map((page) => (
        <PageWithBlocks
          key={page.number}
          page={page}
          pageWidth={pageWidth}
          activeBlock={activeBlock}
          onHover={onHover}
        />
      ))}
    </Document>
  );
}

function PageWithBlocks({
  page,
  pageWidth,
  activeBlock,
  onHover,
}: {
  page: PageDto;
  pageWidth: number;
  activeBlock: string | null;
  onHover: (id: string | null) => void;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  // OCR boxes are in source pixels (page.width). Scale them to the rendered width.
  const scale = page.width > 0 ? pageWidth / page.width : 1;

  return (
    <div ref={containerRef} className="relative mx-auto mb-6 w-fit shadow">
      <Page
        pageNumber={page.number}
        width={pageWidth}
        renderAnnotationLayer={false}
        renderTextLayer={false}
      />
      <div className="pointer-events-none absolute inset-0">
        {page.blocks.map((b) => (
          <div
            key={b.id}
            data-testid="ocr-block"
            title={b.text}
            onMouseEnter={() => onHover(b.id)}
            onMouseLeave={() => onHover(null)}
            className={`pointer-events-auto absolute cursor-help border ${
              activeBlock === b.id
                ? "border-blue-500 bg-blue-500/20"
                : "border-blue-400/40 bg-blue-400/5"
            }`}
            style={{
              left: b.x * scale,
              top: b.y * scale,
              width: b.width * scale,
              height: b.height * scale,
            }}
          />
        ))}
      </div>
    </div>
  );
}
