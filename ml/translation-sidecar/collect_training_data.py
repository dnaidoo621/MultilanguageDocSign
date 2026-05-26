"""
Collect Korean-English legal training data from two sources:
  1. HuggingFace parallel corpora — filter for legal/formal register
  2. Ollama (qwen2.5:7b) — synthetic legal sentence pairs via JSON mode

Output: data/legal_pairs.tsv  (Korean\\tEnglish, UTF-8)
        data/legal_pairs_stats.json (provenance counts)

Usage:
    python collect_training_data.py [--synthetic-only] [--hf-only] [--output data/my.tsv]
"""
from __future__ import annotations
import argparse, csv, json, pathlib, re, sys, time
import requests

DATA_DIR = pathlib.Path(__file__).parent / "data"
DATA_DIR.mkdir(exist_ok=True)

# ---------------------------------------------------------------------------
# Part 1 — HuggingFace parallel corpora
# ---------------------------------------------------------------------------
HF_SOURCES = [
    # (dataset_id, split, ko_col, en_col)
    ("Moo/korean-parallel-corpora",                    "train", "ko",     "en"),
    ("lemon-mint/korean_parallel_sentences_v1.1",      "train", "korean", "english"),
    ("msarmi9/korean-english-multitarget-ted-talks-task", "train", "korean", "english"),
]

LEGAL_KO = [
    "계약", "계약서", "조항", "의무", "권리", "위반", "해지", "보증금",
    "임대", "임차", "근로계약", "고용", "임금", "급여", "해고", "퇴직금",
    "배상", "손해", "위약금", "중재", "준거법", "기밀", "비밀유지",
    "면책", "불가항력", "자동갱신", "지식재산", "저작권", "특허",
    "분쟁", "소송", "법령", "규정", "동의", "승인", "허가", "당사자",
    "갑을", "을의", "갑의", "갑은", "을은", "을이", "갑이",
    "경업금지", "전직금지", "영업비밀", "손해배상", "지급 기한",
]
LEGAL_EN = [
    "contract", "clause", "obligation", "right", "breach", "terminat",
    "deposit", "lease", "rental", "employ", "wage", "salary", "dismiss",
    "sever", "damages", "penalty", "arbitration", "governing law",
    "confidential", "indemnif", "force majeure", "renew", "expir",
    "intellectual property", "copyright", "patent", "dispute", "litigat",
    "provision", "regulation", "consent", "licens", "party a", "party b",
    "non-compet", "trade secret", "assignment", "waiver", "warranty",
    "represent", "entire agreement", "sever", "notice", "invoice",
]

HAS_HANGUL = re.compile(r"[가-힣]")
HAS_CJK    = re.compile(r"[一-鿿㐀-䶿぀-ヿ]")  # Chinese / Japanese


def _is_legal(ko: str, en: str) -> bool:
    en_l = en.lower()
    return any(k in ko for k in LEGAL_KO) or any(k in en_l for k in LEGAL_EN)


def _quality(ko: str, en: str) -> bool:
    if not (10 <= len(ko) <= 300 and 10 <= len(en) <= 300):
        return False
    if "\t" in ko or "\t" in en:
        return False
    if not HAS_HANGUL.search(ko):
        return False
    if HAS_CJK.search(ko) or HAS_CJK.search(en):
        return False
    return True


def collect_hf_pairs(max_per_source: int = 200_000) -> list[tuple[str, str]]:
    try:
        from datasets import load_dataset
    except ImportError:
        print("  datasets not installed — skipping HuggingFace sources")
        return []

    pairs: list[tuple[str, str]] = []
    for ds_id, split, ko_col, en_col in HF_SOURCES:
        try:
            print(f"  Loading {ds_id} ...", flush=True)
            ds = load_dataset(ds_id, split=split)
            n = min(len(ds), max_per_source)
            for row in ds.select(range(n)):
                ko, en = row[ko_col].strip(), row[en_col].strip()
                if _quality(ko, en) and _is_legal(ko, en):
                    pairs.append((ko, en))
            print(f"    → {len(pairs)} legal pairs so far")
        except Exception as exc:
            print(f"  WARNING: {ds_id} failed: {exc}")
    return pairs


# ---------------------------------------------------------------------------
# Part 2 — Ollama synthetic generation (JSON mode)
# ---------------------------------------------------------------------------
OLLAMA_URL = "http://localhost:11434/api/generate"

# Each topic: (name, system_hint, user_prompt)
# We ask for a JSON array of objects: [{"ko": "...", "en": "..."}, ...]
TOPICS = [
    ("lease_deposit",
     "You are a Korean legal translation expert.",
     "Generate 20 realistic Korean legal sentence pairs about 보증금 (security deposit) in lease agreements. "
     "Return a JSON array: [{\"ko\": \"<Korean sentence>\", \"en\": \"<English translation>\"}, ...]. "
     "Use authentic legal phrasing. Include variety: returning deposit, deducting damages, deposit amount, payment schedule."),
    ("lease_renewal",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean legal sentence pairs about 자동갱신 (automatic renewal) and 계약 만료 in lease agreements. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("lease_termination",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean legal sentence pairs about 계약 해지 (contract termination), 해지 통보, 중도 해지 in lease agreements. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("lease_obligations",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean legal sentence pairs about 임대인 (landlord) and 임차인 (tenant) obligations in Korean lease agreements. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("employment_terms",
     "You are a Korean employment law expert.",
     "Generate 20 Korean sentence pairs about employment contract terms: 근로시간, 임금, 직위, 근무장소, 수습기간. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("employment_dismissal",
     "You are a Korean employment law expert.",
     "Generate 20 Korean sentence pairs about 해고 (dismissal), 권고사직, 퇴직금, 해고 예고 in Korean employment contracts. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("employment_leave",
     "You are a Korean employment law expert.",
     "Generate 20 Korean sentence pairs about 연차 유급휴가, 육아휴직, 병가, 4대보험 in Korean employment law. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("nda_obligations",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 기밀유지 (confidentiality) obligations in Korean NDAs. "
     "Use terms: 비밀정보, 공개 금지 의무, 기밀 유지 기간, 기밀 유지 의무 위반. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("nda_remedies",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about NDA breach consequences: 위약금, 손해배상, 금지청구, 계약 해지. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("penalty_damages",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 위약금 (liquidated damages), 손해배상 (damages), 지연손해금 in Korean contracts. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("force_majeure",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 불가항력 (force majeure) in Korean contracts. "
     "Include: 천재지변, 전쟁, 파업, 전염병, 정부 조치, 면책 효과. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("indemnification",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 면책 (indemnification), 책임 제한, 간접손해 배제 in Korean commercial agreements. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("governing_law",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 준거법 (governing law), 관할 법원 (jurisdiction), 중재 조항 in Korean contracts. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("ip_rights",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 지식재산권 (IP rights), 저작권, 특허권, 영업비밀 in Korean agreements. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("contract_parties",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs using 갑 (Party A / 甲) and 을 (Party B / 乙) contract party references. "
     "Show both parties' rights and obligations in realistic contract clauses. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("dispute_resolution",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 분쟁 해결, 중재 (arbitration), 조정 (mediation), 소송 관할 in Korean contracts. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("general_boilerplate",
     "You are a Korean legal translation expert.",
     "Generate 30 Korean sentence pairs for standard boilerplate clauses: 완전합의 조항, 분리가능성, 권리 포기, 서면 변경, 통지 조항, 계약 전체 합의. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("payment_terms",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 지급 조건, 청구서 발행, 연체이자, 지급 기한, 대금 지급 방법 in Korean contracts. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("non_compete",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 경업금지 (non-compete), 전직금지, 영업비밀 보호 in Korean employment/commercial law. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("assignment_amendment",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 계약 변경 및 수정, 양도 및 이전 금지, 제3자 권리 in Korean contracts. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("representations_warranties",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs about 진술 및 보장 (representations and warranties), 사실 확인 의무 in Korean contracts. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("service_agreement",
     "You are a Korean legal translation expert.",
     "Generate 20 Korean sentence pairs for a service agreement (용역 계약서): scope of work, deliverables, acceptance, change orders. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("real_estate_jeonse",
     "You are a Korean legal translation expert specializing in Korean real estate.",
     "Generate 20 Korean sentence pairs about 전세 (jeonse) contracts specifically: 전세금, 전세권, 전세 기간, 전세금 반환, 전세금 인상. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("labor_standards_act",
     "You are a Korean labor law expert.",
     "Generate 20 Korean sentence pairs quoting or paraphrasing articles from the Korean Labor Standards Act (근로기준법): "
     "working hours, minimum wage, rest periods, annual leave, workplace safety. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
    ("commercial_contract_misc",
     "You are a Korean legal translation expert.",
     "Generate 20 diverse Korean sentence pairs covering miscellaneous commercial contract terms: "
     "독점 계약, 하도급, 감사권, 보험 의무, 인허가, 규제 준수. "
     "Return JSON array: [{\"ko\": \"...\", \"en\": \"...\"}, ...]."),
]


def _call_ollama_json(system: str, user: str) -> list[dict]:
    """
    Call Ollama with JSON mode.

    qwen2.5:7b in JSON mode returns a single JSON object, not a bare array.
    We use the schema {"pairs": [...]} so the model fills in the list.
    If the model ignores the schema and returns a bare object with ko/en keys
    (single-pair fallback), we wrap it in a list.
    """
    # Append schema instruction to every prompt.
    user_with_schema = (
        user.rstrip()
        + '\n\nReturn ONLY JSON with this exact schema (no extra text):'
        + ' {"pairs": [{"ko": "Korean sentence", "en": "English translation"}, ...]}'
    )
    payload = {
        "model": "qwen2.5:7b",
        "system": system,
        "prompt": user_with_schema,
        "format": "json",
        "stream": False,
        "options": {"temperature": 0.6, "num_predict": 3000},
    }
    r = requests.post(OLLAMA_URL, json=payload, timeout=180)
    r.raise_for_status()
    raw = r.json().get("response", "")
    data = json.loads(raw)
    if isinstance(data, list):
        return data
    if isinstance(data, dict):
        # Preferred: {"pairs": [...]}
        if "pairs" in data and isinstance(data["pairs"], list):
            return data["pairs"]
        # Single-pair fallback: {"ko": "...", "en": "..."}
        if "ko" in data and "en" in data:
            return [data]
        # Any list value
        for v in data.values():
            if isinstance(v, list):
                return v
    return []


def collect_synthetic_pairs(
    out_path: pathlib.Path | None = None,
    skip_topics: set[str] | None = None,
) -> list[tuple[str, str]]:
    """Generate synthetic pairs, optionally writing each topic's results
    immediately to *out_path* (append mode) so a crash/timeout mid-run
    doesn't lose work already done.

    Pass ``skip_topics`` (a set of topic names) to resume a previous run.
    """
    pairs: list[tuple[str, str]] = []
    skip_topics = skip_topics or set()
    for topic, system, user in TOPICS:
        if topic in skip_topics:
            print(f"  Synthetic [{topic}] ... skipped (already collected)")
            continue
        try:
            print(f"  Synthetic [{topic}] ...", end=" ", flush=True)
            items = _call_ollama_json(system, user)
            added_pairs: list[tuple[str, str]] = []
            for item in items:
                if not isinstance(item, dict):
                    continue
                ko = str(item.get("ko", "")).strip()
                en = str(item.get("en", "")).strip()
                if _quality(ko, en):
                    added_pairs.append((ko, en))
            print(f"{len(added_pairs)} pairs")
            pairs.extend(added_pairs)
            # Write immediately so a later timeout doesn't lose this topic
            if out_path is not None and added_pairs:
                with open(out_path, "a", encoding="utf-8", newline="") as f:
                    w = csv.writer(f, delimiter="\t")
                    for ko, en in added_pairs:
                        w.writerow([ko, en])
        except Exception as exc:
            print(f"ERROR: {exc}")
        time.sleep(0.5)
    return pairs


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def _load_existing_pairs(path: pathlib.Path) -> tuple[list[tuple[str, str]], set[str]]:
    """Return (pairs, ko_seen_set) from an existing TSV, or empty if absent."""
    if not path.exists():
        return [], set()
    pairs: list[tuple[str, str]] = []
    seen: set[str] = set()
    with open(path, encoding="utf-8") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t", 1)
            if len(parts) == 2:
                ko, en = parts[0].strip(), parts[1].strip()
                if ko and ko not in seen:
                    seen.add(ko)
                    pairs.append((ko, en))
    return pairs, seen


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--synthetic-only", action="store_true")
    ap.add_argument("--hf-only",        action="store_true")
    ap.add_argument("--resume",         action="store_true",
                    help="Continue a previous --synthetic-only run: read existing output, "
                         "skip topics whose pairs are already there.")
    ap.add_argument("--output",         default=str(DATA_DIR / "legal_pairs.tsv"))
    args = ap.parse_args()

    out_path = pathlib.Path(args.output)
    stats: dict[str, int] = {}

    hf_pairs: list[tuple[str, str]] = []
    syn_pairs: list[tuple[str, str]] = []

    if not args.synthetic_only:
        print("=== HuggingFace parallel corpora ===")
        hf_pairs = collect_hf_pairs()
        stats["hf_filtered"] = len(hf_pairs)
        print(f"  HF legal pairs: {len(hf_pairs):,}")

    if not args.hf_only:
        print("\n=== Synthetic generation (Ollama qwen2.5:7b) ===")

        # Resume: figure out which topics are already done
        skip_topics: set[str] = set()
        existing_ko: set[str] = set()
        if args.resume and out_path.exists():
            existing_pairs, existing_ko = _load_existing_pairs(out_path)
            print(f"  Resuming — {len(existing_pairs)} pairs already in {out_path.name}")
            # A topic is "done" if we can find any of its likely output already saved.
            # Simplest heuristic: if the output file has ≥15 lines from a topic,
            # the topic was completed.  We can't know that precisely without extra
            # metadata, so instead we track counts per 20-pair chunk: any topic
            # that contributed ≥10 pairs is considered done.
            # Better: store a progress sidecar file.
            progress_path = DATA_DIR / "legal_pairs_progress.json"
            if progress_path.exists():
                with open(progress_path) as pf:
                    done_map: dict[str, int] = json.load(pf)
                skip_topics = {t for t, n in done_map.items() if n >= 0}
                print(f"  Skipping {len(skip_topics)} already-completed topics: "
                      f"{sorted(skip_topics)}")
            syn_pairs = existing_pairs  # start from what we have
        else:
            # Fresh run: truncate/create the file now so incremental appends start clean
            out_path.write_text("", encoding="utf-8")

        # Collect — each completed topic is appended to out_path immediately
        new_syn = collect_synthetic_pairs(
            out_path=out_path,
            skip_topics=skip_topics,
        )
        syn_pairs = syn_pairs + new_syn

        # Save progress sidecar
        progress_path = DATA_DIR / "legal_pairs_progress.json"
        done_map = {}
        if args.resume and progress_path.exists():
            with open(progress_path) as pf:
                done_map = json.load(pf)
        # Mark every attempted topic as done (count = pairs generated; 0 = attempted but got none)
        topic_names = {t[0] for t in TOPICS}
        for t in topic_names - skip_topics:
            done_map[t] = done_map.get(t, 0) + 1
        with open(progress_path, "w") as pf:
            json.dump(done_map, pf, indent=2)

        stats["synthetic"] = len(syn_pairs)
        print(f"\n  Total synthetic legal pairs: {len(syn_pairs):,}")

    # For HF-only or combined runs we still do a single deduped write
    if not args.synthetic_only:
        all_pairs = hf_pairs + syn_pairs
        seen: set[str] = set()
        deduped: list[tuple[str, str]] = []
        for ko, en in all_pairs:
            if ko not in seen:
                seen.add(ko)
                deduped.append((ko, en))
        stats["total_deduped"] = len(deduped)

        with open(out_path, "w", encoding="utf-8", newline="") as f:
            w = csv.writer(f, delimiter="\t")
            for ko, en in deduped:
                w.writerow([ko, en])
        print(f"\n✓ Written {len(deduped):,} pairs → {out_path}")
    else:
        # Synthetic-only: file was written incrementally; just report final count
        final_pairs, _ = _load_existing_pairs(out_path)
        stats["total_deduped"] = len(final_pairs)
        syn_pairs = final_pairs
        print(f"\n✓ {len(final_pairs):,} pairs in {out_path}")

    stats["total_deduped"] = stats.get("total_deduped", len(syn_pairs + hf_pairs))
    with open(DATA_DIR / "legal_pairs_stats.json", "w") as f:
        json.dump(stats, f, indent=2)
    print(f"  Stats: {stats}")

    # Show samples
    print("\n=== Sample pairs ===")
    for ko, en in (syn_pairs or hf_pairs)[:6]:
        print(f"  KO: {ko[:90]}")
        print(f"  EN: {en[:90]}")
        print()


if __name__ == "__main__":
    main()
