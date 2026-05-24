"use client";

import { forwardRef, useEffect, useRef, useState } from "react";
import RiskDot, { type RiskLevel } from "./RiskDot";

export type ReaderClause = {
  id: string;
  ref: string;
  page: number;
  risk: RiskLevel;
  riskType: string;
  explain: string;
  src: string;
  en: string;
};

type Overlay = "badges" | "highlight" | "margin" | "off";

export function BilingualReader({
  clauses,
  layout,
  riskOverlay,
  active,
  pinned,
  onActive,
  onTogglePin,
}: {
  clauses: ReaderClause[];
  layout: "mirrored" | "margin";
  riskOverlay: Overlay;
  active: string | null;
  pinned: string | null;
  onActive: (id: string | null) => void;
  onTogglePin: (id: string) => void;
}) {
  return layout === "mirrored" ? (
    <MirroredReader
      clauses={clauses}
      riskOverlay={riskOverlay}
      active={active}
      pinned={pinned}
      onActive={onActive}
      onTogglePin={onTogglePin}
    />
  ) : (
    <MarginReader clauses={clauses} active={active} pinned={pinned} onActive={onActive} onTogglePin={onTogglePin} />
  );
}

function MirroredReader({
  clauses,
  riskOverlay,
  active,
  pinned,
  onActive,
  onTogglePin,
}: {
  clauses: ReaderClause[];
  riskOverlay: Overlay;
  active: string | null;
  pinned: string | null;
  onActive: (id: string | null) => void;
  onTogglePin: (id: string) => void;
}) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const srcRefs = useRef<Record<string, HTMLDivElement | null>>({});
  const trgRefs = useRef<Record<string, HTMLDivElement | null>>({});
  const [line, setLine] = useState<{ x1: number; y1: number; x2: number; y2: number; w: number; h: number } | null>(null);

  const focus = pinned ?? active;

  useEffect(() => {
    if (!focus || !wrapRef.current) {
      setLine(null);
      return;
    }
    const calc = () => {
      const a = srcRefs.current[focus];
      const b = trgRefs.current[focus];
      const w = wrapRef.current;
      if (!a || !b || !w) return;
      const wr = w.getBoundingClientRect();
      const ar = a.getBoundingClientRect();
      const br = b.getBoundingClientRect();
      setLine({
        x1: ar.right - wr.left,
        y1: ar.top + ar.height / 2 - wr.top,
        x2: br.left - wr.left,
        y2: br.top + br.height / 2 - wr.top,
        w: wr.width,
        h: wr.height,
      });
    };
    calc();
    const ro = new ResizeObserver(calc);
    ro.observe(wrapRef.current);
    window.addEventListener("scroll", calc, true);
    return () => {
      ro.disconnect();
      window.removeEventListener("scroll", calc, true);
    };
  }, [focus, clauses.length]);

  return (
    <div ref={wrapRef} className="mirror">
      <div className="mirror-pane src">
        <PaneHeader label="ORIGINAL · SOURCE" />
        <div className="pane-body">
          {clauses.map((c) => (
            <Clause
              key={"s" + c.id}
              ref={(el) => { srcRefs.current[c.id] = el; }}
              side="src"
              clause={c}
              focus={focus}
              riskOverlay={riskOverlay}
              onEnter={() => !pinned && onActive(c.id)}
              onLeave={() => !pinned && onActive(null)}
              onClick={() => onTogglePin(c.id)}
            />
          ))}
        </div>
      </div>

      <div className="mirror-pane trg">
        <PaneHeader label="TRANSLATION · ENGLISH" />
        <div className="pane-body">
          {clauses.map((c) => (
            <Clause
              key={"t" + c.id}
              ref={(el) => { trgRefs.current[c.id] = el; }}
              side="trg"
              clause={c}
              focus={focus}
              riskOverlay={riskOverlay}
              testid="reader-clause"
              onEnter={() => !pinned && onActive(c.id)}
              onLeave={() => !pinned && onActive(null)}
              onClick={() => onTogglePin(c.id)}
            />
          ))}
        </div>
      </div>

      {line && (
        <svg className="kinship" width={line.w} height={line.h} viewBox={`0 0 ${line.w} ${line.h}`}>
          <defs>
            <linearGradient id="kgrad" x1="0%" x2="100%">
              <stop offset="0%" stopColor="var(--stamp)" stopOpacity="0" />
              <stop offset="20%" stopColor="var(--stamp)" stopOpacity="0.9" />
              <stop offset="80%" stopColor="var(--stamp)" stopOpacity="0.9" />
              <stop offset="100%" stopColor="var(--stamp)" stopOpacity="0" />
            </linearGradient>
          </defs>
          <path
            d={`M ${line.x1} ${line.y1} C ${(line.x1 + line.x2) / 2} ${line.y1}, ${(line.x1 + line.x2) / 2} ${line.y2}, ${line.x2} ${line.y2}`}
            stroke="url(#kgrad)"
            strokeWidth="1.5"
            fill="none"
            strokeDasharray="4 4"
          />
          <circle cx={line.x1} cy={line.y1} r="3" fill="var(--stamp)" />
          <circle cx={line.x2} cy={line.y2} r="3" fill="var(--stamp)" />
        </svg>
      )}
    </div>
  );
}

function PaneHeader({ label }: { label: string }) {
  return (
    <div className="pane-header">
      <div className="mono">{label}</div>
      <div className="pane-corner tl" />
      <div className="pane-corner tr" />
    </div>
  );
}

const Clause = forwardRef<
  HTMLDivElement,
  {
    side: "src" | "trg";
    clause: ReaderClause;
    focus: string | null;
    riskOverlay: Overlay;
    testid?: string;
    onEnter: () => void;
    onLeave: () => void;
    onClick: () => void;
  }
>(function Clause({ side, clause, focus, riskOverlay, testid, onEnter, onLeave, onClick }, ref) {
  const isFocus = focus === clause.id;
  const isDim = !!focus && focus !== clause.id;
  const risky = clause.risk !== "none";
  const showBadge = riskOverlay === "badges" && risky;
  const useHighlight = riskOverlay === "highlight" && risky;
  const useMarginNote = riskOverlay === "margin" && risky;

  return (
    <div
      ref={ref}
      data-testid={testid}
      className={"clause " + (isFocus ? "focus " : "") + (isDim ? "dim " : "") + (useHighlight ? `hl-${clause.risk}` : "")}
      onMouseEnter={onEnter}
      onMouseLeave={onLeave}
      onClick={onClick}
    >
      <div className="clause-meta">
        <span className="clause-ref mono">{clause.ref}</span>
        <span className="clause-page mono-meta">PG.{clause.page}</span>
        {risky && (
          <span className="clause-dot">
            <RiskDot level={clause.risk} />
          </span>
        )}
      </div>

      {showBadge && (
        <span className={`risk-pill ${clause.risk[0]}`} style={{ display: "inline-block", marginBottom: 6 }}>
          {clause.risk.toUpperCase()} · {clause.riskType}
        </span>
      )}

      <p className={"clause-text " + (side === "src" ? "src-text" : "trg-text")} lang={side === "src" ? "ko" : "en"}>
        {side === "src" ? clause.src : clause.en}
      </p>

      {side === "trg" && clause.explain && (riskOverlay === "badges" || riskOverlay === "highlight") && isFocus && (
        <div className="clause-explain">
          <span className="mono">! WHY</span>
          <p>{clause.explain}</p>
        </div>
      )}

      {useMarginNote && side === "trg" && (
        <div className={`margin-note ${clause.risk}`}>
          <div className="margin-note-head">
            <RiskDot level={clause.risk} />
            <span className="mono">{clause.riskType?.toUpperCase()}</span>
          </div>
          <p>{clause.explain}</p>
        </div>
      )}
    </div>
  );
});

function MarginReader({
  clauses,
  active,
  pinned,
  onActive,
  onTogglePin,
}: {
  clauses: ReaderClause[];
  active: string | null;
  pinned: string | null;
  onActive: (id: string | null) => void;
  onTogglePin: (id: string) => void;
}) {
  const focus = pinned ?? active;
  return (
    <div className="margin-doc">
      <div className="margin-doc-header">
        <div className="mono">ANNOTATED · SOURCE → EN</div>
        <span className="badge-mono">DRAFT · UNSIGNED</span>
      </div>

      {clauses.map((c) => {
        const isFocus = focus === c.id;
        const isDim = !!focus && focus !== c.id;
        return (
          <article
            key={c.id}
            data-testid="reader-clause"
            className={"margin-clause " + (isFocus ? "focus " : "") + (isDim ? "dim " : "")}
            onMouseEnter={() => !pinned && onActive(c.id)}
            onMouseLeave={() => !pinned && onActive(null)}
            onClick={() => onTogglePin(c.id)}
          >
            <div className="margin-num">
              <span className="mono">{c.ref}</span>
              <span className="mono-meta">PG.{c.page}</span>
            </div>
            <div className="margin-body">
              <p className="margin-en serif" lang="en">
                {c.en}
              </p>
              <p className="margin-ko mono-meta" lang="ko">
                {c.src}
              </p>
            </div>
            <div className="margin-side">
              {c.risk !== "none" && (
                <div className={`m-note ${c.risk}`}>
                  <div className="row" style={{ gap: 6 }}>
                    <RiskDot level={c.risk} />
                    <span className="mono" style={{ fontSize: 9 }}>
                      {c.risk.toUpperCase()}
                    </span>
                  </div>
                  <div className="mono-meta" style={{ color: "var(--ink-2)", fontWeight: 600, marginTop: 4 }}>
                    {c.riskType}
                  </div>
                  <p className="m-note-explain">{c.explain}</p>
                </div>
              )}
            </div>
          </article>
        );
      })}
    </div>
  );
}
