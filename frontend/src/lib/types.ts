export interface DocumentSummary {
  id: string;
  fileName: string;
  status: string;
  sourceLanguage: string | null;
  pageCount: number;
  createdAt: string;
}

export interface BlockDto {
  id: string;
  order: number;
  text: string;
  language: string | null;
  confidence: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface PageDto {
  number: number;
  width: number;
  height: number;
  blocks: BlockDto[];
}

export interface DocumentDetail {
  id: string;
  fileName: string;
  status: string;
  sourceLanguage: string | null;
  pageCount: number;
  error: string | null;
  createdAt: string;
  pages: PageDto[];
}

export interface TranslationSegmentDto {
  sourceBlockId: string;
  pageNumber: number;
  order: number;
  sourceText: string;
  translatedText: string;
}

export interface TranslationDetail {
  id: string;
  documentId: string;
  targetLanguage: string;
  sourceLanguage: string | null;
  status: string;
  model: string | null;
  error: string | null;
  segments: TranslationSegmentDto[];
}
