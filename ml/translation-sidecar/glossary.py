"""
Legal-term glossary for the translation sidecar.

Two mechanisms work together:

1. target_prefix (CTranslate2 constrained decoding)
   Applied when a known Korean term is the very first content of a block — forces the
   decoder to open with the canonical English rendering before continuing freely.
   Only applied for block-initial terms to avoid garbled output on mid-sentence terms.

2. post_process_substitution
   After translation, scan for known Korean terms in the original source that did NOT
   make it into the output as expected. Apply a curated substitution table that maps
   common mistranslations to the canonical form.  This catches mid-sentence occurrences
   that target_prefix cannot handle without mangling word order.
"""

from __future__ import annotations

# ---------------------------------------------------------------------------
# Korean → English legal glossary
# ---------------------------------------------------------------------------
# Key: Korean term (exact substring match in source text)
# Value: canonical English rendering
GLOSSARY_KO_EN: dict[str, str] = {
    "준거법": "governing law",
    "중재": "arbitration",
    "면책": "indemnification",
    "기밀유지": "confidentiality",
    "불가항력": "force majeure",
    "해지": "termination",
    "해지권": "right of termination",
    "보증금": "security deposit",
    "위약금": "penalty",
    "자동갱신": "automatic renewal",
    "갑": "Party A",
    "을": "Party B",
}

# Register all language-pair glossaries here.
# Key: "{src_lang}-{tgt_lang}" (lowercase)
GLOSSARIES: dict[str, dict[str, str]] = {
    "ko-en": GLOSSARY_KO_EN,
}

# Common mistranslations to substitute after translation.
# Maps bad_translation → canonical_translation.
# Only applied when the corresponding Korean term is present in the source block
# AND the canonical English is NOT already in the output.
_SUBSTITUTIONS: dict[str, list[tuple[str, str]]] = {
    "ko-en": [
        # 준거법 (governing law) — opus-mt-ko-en often romanises or misdescribes it
        ("applicable law", "governing law"),
        ("governing act", "governing law"),
        ("jun law", "governing law"),
        ("jun-law", "governing law"),
        ("zingang", "governing law"),
        # 중재 (arbitration) — often rendered as "intercession", "mediation", "inter media", "intercede"
        ("intercession", "arbitration"),
        ("inter media", "arbitration"),
        ("intermediate", "arbitration"),
        ("intercede", "arbitration"),
        ("mediation", "arbitration"),
        ("arbitral", "arbitration"),
        # 면책 (indemnification)
        ("exemption from liability", "indemnification"),
        ("exemption", "indemnification"),
        # 해지 (termination)
        ("cancellation", "termination"),
        ("rescission", "termination"),
        # 보증금 (security deposit)
        ("deposit money", "security deposit"),
        ("guarantee money", "security deposit"),
        # 위약금 (penalty) — often rendered as "counterfeit", "breach money", "default money"
        ("counterfeit payment", "penalty"),
        ("counterfeit", "penalty"),
        ("breach money", "penalty"),
        ("default money", "penalty"),
        ("penalty clause", "penalty"),
        ("liquidated damages", "penalty"),
        # 자동갱신 (automatic renewal) — also "automatically updated", "auto-extend"
        ("auto renewal", "automatic renewal"),
        ("auto-renewal", "automatic renewal"),
        ("automatically updated", "automatically renewed"),
        ("auto-extend", "automatic renewal"),
    ],
}


def get_target_prefix_tokens(
    source_text: str,
    src_lang: str,
    tgt_lang: str,
    sp_target,
) -> list[str]:
    """
    Return CTranslate2 target_prefix token list if a known glossary term opens the block,
    or an empty list if no constraint should be applied.

    CTranslate2 prepends the BOS token internally before the prefix, so we only return
    the surface tokens of the English phrase (no <pad> / BOS sentinel needed).

    Args:
        source_text: raw source block text (before tokenization).
        src_lang, tgt_lang: ISO 639-1 language codes.
        sp_target: sentencepiece.SentencePieceProcessor for the target language.

    Returns:
        list of target subword piece strings, or [] if no constraint applies.
    """
    pair = f"{src_lang}-{tgt_lang}"
    glossary = GLOSSARIES.get(pair)
    if not glossary:
        return []

    stripped = source_text.strip()
    for ko_term, en_phrase in glossary.items():
        if stripped.startswith(ko_term):
            # Encode the English phrase to subword pieces.
            return sp_target.encode_as_pieces(en_phrase)

    return []


def post_process(
    source_text: str,
    translated_text: str,
    src_lang: str,
    tgt_lang: str,
) -> str:
    """
    Apply glossary substitutions to catch mid-sentence legal terms the model translated
    differently from the canonical form.

    Only modifies the translation when:
    - The Korean term is present in the source (so we know it's relevant).
    - The translation contains a known "bad" rendering.
    - The canonical rendering is NOT already in the translation (avoid double-replace).

    Substitutions are case-insensitive on the output side.
    """
    pair = f"{src_lang}-{tgt_lang}"
    glossary = GLOSSARIES.get(pair)
    substitutions = _SUBSTITUTIONS.get(pair, [])
    if not glossary or not substitutions:
        return translated_text

    result = translated_text
    for ko_term, canonical in glossary.items():
        if ko_term not in source_text:
            continue
        # Canonical is already there — nothing to do.
        if canonical.lower() in result.lower():
            continue
        # Try each known bad form.
        for bad, good in substitutions:
            if bad.lower() in result.lower():
                # Case-preserving replace: match the casing of what we found.
                import re
                result = re.sub(re.escape(bad), good, result, flags=re.IGNORECASE)
                break  # apply at most one substitution per Korean term

    return result
