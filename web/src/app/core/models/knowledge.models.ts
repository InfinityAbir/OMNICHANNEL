export interface KnowledgeDocumentResponse {
  id: string;
  title: string;
  version: number;
  status: string;
  chunkCount: number;
  updatedAt: string;
}

export interface KnowledgeSearchResultResponse {
  documentId: string;
  documentTitle: string;
  chunkText: string;
  distance: number;
}
