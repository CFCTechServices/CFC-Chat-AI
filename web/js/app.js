// Entry point: initialize all features
import { initTabs } from './tabs.js';
import { initSingleUpload } from './upload-single.js';
import { initBulkUpload } from './upload-bulk.js';
import { initSearch } from './search.js';
import { initAsk } from './ask.js';

// Initialize features
initTabs();

initSingleUpload({
  dropzoneId: 'dropzone',
  fileInputId: 'fileInput',
  uploadsId: 'uploads',
});

initBulkUpload({
  bulkInputId: 'bulkInput',
  bulkStartId: 'bulkStart',
  bulkUploadsId: 'bulkUploads',
});

initSearch({
  searchFormId: 'searchForm',
  searchQueryId: 'searchQuery',
  searchResultsId: 'searchResults',
});

initAsk({
  askFormId: 'askForm',
  askQuestionId: 'askQuestion',
  askSendId: 'askSend',
  askThreadId: 'askThread',
});
