-- WARNING: This schema is for context only and is not meant to be run.
-- Table order and constraints may not be valid for execution.

CREATE TABLE public.allowed_users (
  updated_at timestamp with time zone DEFAULT now(),
  email text NOT NULL,
  role text NOT NULL DEFAULT 'user'::text,
  status text NOT NULL DEFAULT 'active'::text,
  created_at timestamp with time zone NOT NULL DEFAULT timezone('utc'::text, now()),
  CONSTRAINT allowed_users_pkey PRIMARY KEY (email)
);
CREATE TABLE public.users (
  email text NOT NULL UNIQUE,
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  role text NOT NULL DEFAULT 'user'::text,
  created_at timestamp with time zone NOT NULL DEFAULT timezone('utc'::text, now()),
  updated_at timestamp with time zone DEFAULT now(),
  CONSTRAINT users_pkey PRIMARY KEY (id)
);
CREATE TABLE public.sessions (
  id text NOT NULL,
  user_id uuid NOT NULL,
  expires_at timestamp with time zone NOT NULL,
  revoked_at timestamp with time zone,
  ip_address text,
  user_agent text,
  created_at timestamp with time zone DEFAULT now(),
  CONSTRAINT sessions_pkey PRIMARY KEY (id),
  CONSTRAINT sessions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id)
);
CREATE TABLE public.interactions (
  user_id uuid,
  route text NOT NULL,
  status text NOT NULL,
  duration_ms integer NOT NULL,
  tokens_used integer,
  meta jsonb,
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  created_at timestamp with time zone DEFAULT now(),
  CONSTRAINT interactions_pkey PRIMARY KEY (id),
  CONSTRAINT interactions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id)
);
CREATE TABLE public.profiles (
  id uuid NOT NULL,
  email text,
  full_name text,
  avatar_url text,
  role text DEFAULT 'user'::text CHECK (role = ANY (ARRAY['user'::text, 'dev'::text, 'admin'::text, 'superuser'::text])),
  created_at timestamp with time zone DEFAULT now(),
  deleted_at timestamp with time zone,
  deleted_by uuid,
  status text DEFAULT 'active'::text CHECK (status = ANY (ARRAY['active'::text, 'inactive'::text, 'deleted'::text])),
  CONSTRAINT profiles_pkey PRIMARY KEY (id),
  CONSTRAINT profiles_id_fkey FOREIGN KEY (id) REFERENCES auth.users(id),
  CONSTRAINT profiles_deleted_by_fkey FOREIGN KEY (deleted_by) REFERENCES public.profiles(id)
);
CREATE TABLE public.invitations (
  expires_at timestamp with time zone DEFAULT (now() + '7 days'::interval),
  created_by uuid,
  used_by uuid,
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  created_at timestamp with time zone DEFAULT now(),
  is_registered boolean DEFAULT false,
  email text NOT NULL,
  CONSTRAINT invitations_pkey PRIMARY KEY (id),
  CONSTRAINT invitations_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.profiles(id),
  CONSTRAINT invitations_used_by_fkey FOREIGN KEY (used_by) REFERENCES public.profiles(id)
);
CREATE TABLE public.chat_sessions (
  user_id uuid NOT NULL,
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  title text DEFAULT 'New Chat'::text,
  created_at timestamp with time zone DEFAULT now(),
  updated_at timestamp with time zone DEFAULT now(),
  CONSTRAINT chat_sessions_pkey PRIMARY KEY (id),
  CONSTRAINT chat_sessions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.profiles(id)
);
CREATE TABLE public.chat_messages (
  session_id uuid NOT NULL,
  role text CHECK (role = ANY (ARRAY['user'::text, 'assistant'::text, 'system'::text])),
  content text NOT NULL,
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  citations jsonb DEFAULT '{}'::jsonb,
  created_at timestamp with time zone DEFAULT now(),
  metadata jsonb,
  CONSTRAINT chat_messages_pkey PRIMARY KEY (id),
  CONSTRAINT chat_messages_session_id_fkey FOREIGN KEY (session_id) REFERENCES public.chat_sessions(id)
);
CREATE TABLE public.feedback (
  updated_at timestamp with time zone DEFAULT now(),
  message_id uuid NOT NULL,
  score integer CHECK (score = ANY (ARRAY['-1'::integer, 1])),
  comment text,
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  created_at timestamp with time zone DEFAULT now(),
  user_id uuid,
  CONSTRAINT feedback_pkey PRIMARY KEY (id),
  CONSTRAINT feedback_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.profiles(id),
  CONSTRAINT feedback_message_id_fkey FOREIGN KEY (message_id) REFERENCES public.chat_messages(id)
);
CREATE TABLE public.document_chunks (
  chunk_id text NOT NULL,
  doc_id text NOT NULL,
  section_id text,
  content text,
  source text,
  source_type text,
  image_paths jsonb,
  created_at timestamp without time zone DEFAULT now(),
  section_title text,
  start_seconds double precision,
  end_seconds double precision,
  video_url text,
  txt_url text,
  srt_url text,
  vtt_url text,
  updated_at timestamp with time zone,
  CONSTRAINT document_chunks_pkey PRIMARY KEY (chunk_id)
);
CREATE TABLE public.chunk_feedback_scores (
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  chunk_id text NOT NULL,
  positive_count integer NOT NULL DEFAULT 0,
  negative_count integer NOT NULL DEFAULT 0,
  net_score integer DEFAULT (positive_count - negative_count),
  last_updated timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT chunk_feedback_scores_pkey PRIMARY KEY (chunk_id),
  CONSTRAINT chunk_feedback_scores_chunk_id_fkey FOREIGN KEY (chunk_id) REFERENCES public.document_chunks(chunk_id)
);
CREATE TABLE public.chunk_feedback_events (
  chunk_id text NOT NULL,
  message_id uuid NOT NULL,
  rating smallint NOT NULL CHECK (rating = ANY (ARRAY[1, '-1'::integer])),
  query_embedding USER-DEFINED NOT NULL,
  id bigint NOT NULL DEFAULT nextval('chunk_feedback_events_id_seq'::regclass),
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT chunk_feedback_events_pkey PRIMARY KEY (id),
  CONSTRAINT chunk_feedback_events_chunk_id_fkey FOREIGN KEY (chunk_id) REFERENCES public.document_chunks(chunk_id),
  CONSTRAINT chunk_feedback_events_message_id_fkey FOREIGN KEY (message_id) REFERENCES public.chat_messages(id)
);