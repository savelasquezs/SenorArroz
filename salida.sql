--
-- PostgreSQL database dump
--

\restrict ySA2eBkqMXIw6DrmhgCwARPfNcxkgH15eB7p8rKo1XSKuQI7OITqkscOfsw8zqg

-- Dumped from database version 17.6
-- Dumped by pg_dump version 17.6

-- Started on 2025-11-13 23:19:47

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- TOC entry 261 (class 1255 OID 16776)
-- Name: calculate_expense_detail_total(); Type: FUNCTION; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE FUNCTION public.calculate_expense_detail_total() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.total = NEW.quantity * NEW.amount;
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.calculate_expense_detail_total() OWNER TO postgres;

--
-- TOC entry 260 (class 1255 OID 16774)
-- Name: calculate_order_detail_subtotal(); Type: FUNCTION; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE FUNCTION public.calculate_order_detail_subtotal() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.subtotal = (NEW.quantity * NEW.unit_price) - COALESCE(NEW.discount, 0);
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.calculate_order_detail_subtotal() OWNER TO postgres;

--
-- TOC entry 263 (class 1255 OID 16782)
-- Name: update_expense_header_total(); Type: FUNCTION; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE FUNCTION public.update_expense_header_total() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    header_total INTEGER;
    target_header_id INTEGER;
BEGIN
    IF TG_OP = 'DELETE' THEN
        target_header_id = OLD.header_id;
    ELSE
        target_header_id = NEW.header_id;
    END IF;
    
    SELECT COALESCE(SUM(total), 0)
    INTO header_total
    FROM expense_detail 
    WHERE header_id = target_header_id;
    
    UPDATE expense_header 
    SET 
        total = header_total,
        updated_at = NOW()
    WHERE id = target_header_id;
    
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    ELSE
        RETURN NEW;
    END IF;
END;
$$;


ALTER FUNCTION public.update_expense_header_total() OWNER TO postgres;

--
-- TOC entry 264 (class 1255 OID 16786)
-- Name: update_order_total_on_delivery_fee_change(); Type: FUNCTION; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE FUNCTION public.update_order_total_on_delivery_fee_change() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD.delivery_fee IS DISTINCT FROM NEW.delivery_fee THEN
        NEW.total = NEW.subtotal + COALESCE(NEW.delivery_fee, 0) - NEW.discount_total;
    END IF;
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.update_order_total_on_delivery_fee_change() OWNER TO postgres;

--
-- TOC entry 262 (class 1255 OID 16778)
-- Name: update_order_totals(); Type: FUNCTION; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE FUNCTION public.update_order_totals() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    order_subtotal INTEGER;
    order_discount_total INTEGER;
    order_delivery_fee INTEGER;
BEGIN
    DECLARE
        target_order_id INTEGER;
    BEGIN
        IF TG_OP = 'DELETE' THEN
            target_order_id = OLD.order_id;
        ELSE
            target_order_id = NEW.order_id;
        END IF;
        
        SELECT COALESCE(SUM(subtotal), 0)
        INTO order_subtotal
        FROM order_detail 
        WHERE order_id = target_order_id;
        
        SELECT COALESCE(SUM(discount), 0)
        INTO order_discount_total
        FROM order_detail 
        WHERE order_id = target_order_id;
        
        SELECT COALESCE(delivery_fee, 0)
        INTO order_delivery_fee
        FROM "order"
        WHERE id = target_order_id;
        
        UPDATE "order" 
        SET 
            subtotal = order_subtotal,
            discount_total = order_discount_total,
            total = order_subtotal + order_delivery_fee - order_discount_total,
            updated_at = NOW()
        WHERE id = target_order_id;
    END;
    
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    ELSE
        RETURN NEW;
    END IF;
END;
$$;


ALTER FUNCTION public.update_order_totals() OWNER TO postgres;

--
-- TOC entry 259 (class 1255 OID 16764)
-- Name: update_updated_at_column(); Type: FUNCTION; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE FUNCTION public.update_updated_at_column() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.update_updated_at_column() OWNER TO postgres;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 224 (class 1259 OID 16428)
-- Name: address; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.address (
    id integer NOT NULL,
    customer_id integer NOT NULL,
    neighborhood_id integer NOT NULL,
    address character varying(200) NOT NULL,
    additional_info character varying(150),
    delivery_fee integer NOT NULL,
    latitude numeric(10,6),
    longitude numeric(10,6),
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.address OWNER TO postgres;

--
-- TOC entry 223 (class 1259 OID 16427)
-- Dependencies: 224
-- Name: address_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.address_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.address_id_seq OWNER TO postgres;

--
-- TOC entry 5195 (class 0 OID 0)
-- Dependencies: 223
-- Name: address_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.address_id_seq OWNED BY public.address.id;


--
-- TOC entry 234 (class 1259 OID 16509)
-- Name: app; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.app (
    id integer NOT NULL,
    bank_id integer NOT NULL,
    name character varying(150) NOT NULL,
    image_url character varying(200),
    active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now(),
    deleted_at timestamp without time zone
);


ALTER TABLE public.app OWNER TO postgres;

--
-- TOC entry 233 (class 1259 OID 16508)
-- Dependencies: 234
-- Name: app_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.app_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.app_id_seq OWNER TO postgres;

--
-- TOC entry 5196 (class 0 OID 0)
-- Dependencies: 233
-- Name: app_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.app_id_seq OWNED BY public.app.id;


--
-- TOC entry 242 (class 1259 OID 16610)
-- Name: app_payment; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.app_payment (
    id integer NOT NULL,
    order_id integer NOT NULL,
    app_id integer NOT NULL,
    amount integer NOT NULL,
    is_setted boolean DEFAULT false,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.app_payment OWNER TO postgres;

--
-- TOC entry 241 (class 1259 OID 16609)
-- Dependencies: 242
-- Name: app_payment_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.app_payment_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.app_payment_id_seq OWNER TO postgres;

--
-- TOC entry 5197 (class 0 OID 0)
-- Dependencies: 241
-- Name: app_payment_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.app_payment_id_seq OWNED BY public.app_payment.id;


--
-- TOC entry 232 (class 1259 OID 16494)
-- Name: bank; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.bank (
    id integer NOT NULL,
    branch_id integer NOT NULL,
    name character varying(150) NOT NULL,
    image_url character varying(200),
    active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.bank OWNER TO postgres;

--
-- TOC entry 231 (class 1259 OID 16493)
-- Dependencies: 232
-- Name: bank_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.bank_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.bank_id_seq OWNER TO postgres;

--
-- TOC entry 5198 (class 0 OID 0)
-- Dependencies: 231
-- Name: bank_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.bank_id_seq OWNED BY public.bank.id;


--
-- TOC entry 244 (class 1259 OID 16630)
-- Name: bank_payment; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.bank_payment (
    id integer NOT NULL,
    order_id integer NOT NULL,
    bank_id integer NOT NULL,
    amount numeric(12,2) NOT NULL,
    is_verified boolean DEFAULT false,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.bank_payment OWNER TO postgres;

--
-- TOC entry 243 (class 1259 OID 16629)
-- Dependencies: 244
-- Name: bank_payment_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.bank_payment_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.bank_payment_id_seq OWNER TO postgres;

--
-- TOC entry 5199 (class 0 OID 0)
-- Dependencies: 243
-- Name: bank_payment_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.bank_payment_id_seq OWNED BY public.bank_payment.id;


--
-- TOC entry 218 (class 1259 OID 16390)
-- Name: branch; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.branch (
    id integer NOT NULL,
    name character varying(100) NOT NULL,
    address character varying(200) NOT NULL,
    phone1 character varying(10) NOT NULL,
    phone2 character varying(10),
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.branch OWNER TO postgres;

--
-- TOC entry 217 (class 1259 OID 16389)
-- Dependencies: 218
-- Name: branch_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.branch_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.branch_id_seq OWNER TO postgres;

--
-- TOC entry 5200 (class 0 OID 0)
-- Dependencies: 217
-- Name: branch_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.branch_id_seq OWNED BY public.branch.id;


--
-- TOC entry 220 (class 1259 OID 16399)
-- Name: customer; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.customer (
    id integer NOT NULL,
    branch_id integer NOT NULL,
    name character varying(150) NOT NULL,
    phone1 character varying(10) NOT NULL,
    phone2 character varying(10),
    active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.customer OWNER TO postgres;

--
-- TOC entry 219 (class 1259 OID 16398)
-- Dependencies: 220
-- Name: customer_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.customer_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.customer_id_seq OWNER TO postgres;

--
-- TOC entry 5201 (class 0 OID 0)
-- Dependencies: 219
-- Name: customer_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.customer_id_seq OWNED BY public.customer.id;


--
-- TOC entry 250 (class 1259 OID 16670)
-- Name: expense; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.expense (
    id integer NOT NULL,
    name character varying(100) NOT NULL,
    category_id integer NOT NULL,
    unit character varying(50) DEFAULT 'unit'::character varying NOT NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now(),
    CONSTRAINT expense_unit_check CHECK (((unit)::text = ANY ((ARRAY['unit'::character varying, 'kilo'::character varying, 'package'::character varying, 'pound'::character varying, 'gallon'::character varying])::text[])))
);


ALTER TABLE public.expense OWNER TO postgres;

--
-- TOC entry 256 (class 1259 OID 16725)
-- Name: expense_bank_payment; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.expense_bank_payment (
    id integer NOT NULL,
    bank_id integer NOT NULL,
    expense_header_id integer NOT NULL,
    amount numeric(12,2) NOT NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.expense_bank_payment OWNER TO postgres;

--
-- TOC entry 255 (class 1259 OID 16724)
-- Dependencies: 256
-- Name: expense_bank_payment_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.expense_bank_payment_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.expense_bank_payment_id_seq OWNER TO postgres;

--
-- TOC entry 5202 (class 0 OID 0)
-- Dependencies: 255
-- Name: expense_bank_payment_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.expense_bank_payment_id_seq OWNED BY public.expense_bank_payment.id;


--
-- TOC entry 248 (class 1259 OID 16661)
-- Name: expense_category; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.expense_category (
    id integer NOT NULL,
    name character varying(100) NOT NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.expense_category OWNER TO postgres;

--
-- TOC entry 247 (class 1259 OID 16660)
-- Dependencies: 248
-- Name: expense_category_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.expense_category_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.expense_category_id_seq OWNER TO postgres;

--
-- TOC entry 5203 (class 0 OID 0)
-- Dependencies: 247
-- Name: expense_category_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.expense_category_id_seq OWNED BY public.expense_category.id;


--
-- TOC entry 254 (class 1259 OID 16705)
-- Name: expense_detail; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.expense_detail (
    id integer NOT NULL,
    header_id integer NOT NULL,
    expense_id integer NOT NULL,
    quantity integer DEFAULT 1 NOT NULL,
    amount integer NOT NULL,
    total integer,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.expense_detail OWNER TO postgres;

--
-- TOC entry 253 (class 1259 OID 16704)
-- Dependencies: 254
-- Name: expense_detail_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.expense_detail_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.expense_detail_id_seq OWNER TO postgres;

--
-- TOC entry 5204 (class 0 OID 0)
-- Dependencies: 253
-- Name: expense_detail_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.expense_detail_id_seq OWNED BY public.expense_detail.id;


--
-- TOC entry 252 (class 1259 OID 16686)
-- Name: expense_header; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.expense_header (
    id integer NOT NULL,
    branch_id integer NOT NULL,
    supplier_id integer NOT NULL,
    total integer,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.expense_header OWNER TO postgres;

--
-- TOC entry 251 (class 1259 OID 16685)
-- Dependencies: 252
-- Name: expense_header_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.expense_header_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.expense_header_id_seq OWNER TO postgres;

--
-- TOC entry 5205 (class 0 OID 0)
-- Dependencies: 251
-- Name: expense_header_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.expense_header_id_seq OWNED BY public.expense_header.id;


--
-- TOC entry 249 (class 1259 OID 16669)
-- Dependencies: 250
-- Name: expense_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.expense_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.expense_id_seq OWNER TO postgres;

--
-- TOC entry 5206 (class 0 OID 0)
-- Dependencies: 249
-- Name: expense_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.expense_id_seq OWNED BY public.expense.id;


--
-- TOC entry 236 (class 1259 OID 16524)
-- Name: loyalty_rule; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.loyalty_rule (
    id integer NOT NULL,
    branch_id integer NOT NULL,
    description character varying NOT NULL,
    n_orders_needed integer NOT NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.loyalty_rule OWNER TO postgres;

--
-- TOC entry 235 (class 1259 OID 16523)
-- Dependencies: 236
-- Name: loyalty_rule_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.loyalty_rule_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.loyalty_rule_id_seq OWNER TO postgres;

--
-- TOC entry 5207 (class 0 OID 0)
-- Dependencies: 235
-- Name: loyalty_rule_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.loyalty_rule_id_seq OWNED BY public.loyalty_rule.id;


--
-- TOC entry 222 (class 1259 OID 16414)
-- Name: neighborhood; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.neighborhood (
    id integer NOT NULL,
    branch_id integer NOT NULL,
    name character varying(150) NOT NULL,
    delivery_fee integer NOT NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.neighborhood OWNER TO postgres;

--
-- TOC entry 221 (class 1259 OID 16413)
-- Dependencies: 222
-- Name: neighborhood_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.neighborhood_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.neighborhood_id_seq OWNER TO postgres;

--
-- TOC entry 5208 (class 0 OID 0)
-- Dependencies: 221
-- Name: neighborhood_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.neighborhood_id_seq OWNED BY public.neighborhood.id;


--
-- TOC entry 238 (class 1259 OID 16540)
-- Name: order; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public."order" (
    id integer NOT NULL,
    branch_id integer NOT NULL,
    taken_by_id integer NOT NULL,
    customer_id integer,
    address_id integer,
    loyalty_rule_id integer,
    delivery_man_id integer,
    type character varying NOT NULL,
    delivery_fee integer,
    reserved_for timestamp without time zone,
    status character varying NOT NULL,
    status_times jsonb,
    subtotal integer DEFAULT 0 NOT NULL,
    total integer DEFAULT 0 NOT NULL,
    discount_total integer DEFAULT 0 NOT NULL,
    notes character varying(200),
    cancelled_reason character varying(200),
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now(),
    CONSTRAINT order_status_check CHECK (((status)::text = ANY ((ARRAY['taken'::character varying, 'in_preparation'::character varying, 'ready'::character varying, 'on_the_way'::character varying, 'delivered'::character varying, 'cancelled'::character varying])::text[]))),
    CONSTRAINT order_type_check CHECK (((type)::text = ANY ((ARRAY['onsite'::character varying, 'delivery'::character varying, 'reservation'::character varying])::text[])))
);


ALTER TABLE public."order" OWNER TO postgres;

--
-- TOC entry 240 (class 1259 OID 16586)
-- Name: order_detail; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.order_detail (
    id integer NOT NULL,
    order_id integer NOT NULL,
    product_id integer NOT NULL,
    quantity integer DEFAULT 1,
    unit_price integer DEFAULT 0,
    discount integer DEFAULT 0,
    subtotal integer,
    notes character varying,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.order_detail OWNER TO postgres;

--
-- TOC entry 239 (class 1259 OID 16585)
-- Dependencies: 240
-- Name: order_detail_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.order_detail_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.order_detail_id_seq OWNER TO postgres;

--
-- TOC entry 5209 (class 0 OID 0)
-- Dependencies: 239
-- Name: order_detail_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.order_detail_id_seq OWNED BY public.order_detail.id;


--
-- TOC entry 237 (class 1259 OID 16539)
-- Dependencies: 238
-- Name: order_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.order_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.order_id_seq OWNER TO postgres;

--
-- TOC entry 5210 (class 0 OID 0)
-- Dependencies: 237
-- Name: order_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.order_id_seq OWNED BY public."order".id;


--
-- TOC entry 228 (class 1259 OID 16461)
-- Name: product; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.product (
    id integer NOT NULL,
    category_id integer NOT NULL,
    name character varying(150) NOT NULL,
    price integer NOT NULL,
    stock integer,
    active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.product OWNER TO postgres;

--
-- TOC entry 226 (class 1259 OID 16447)
-- Name: product_category; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.product_category (
    id integer NOT NULL,
    branch_id integer NOT NULL,
    name character varying(150) NOT NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.product_category OWNER TO postgres;

--
-- TOC entry 225 (class 1259 OID 16446)
-- Dependencies: 226
-- Name: product_category_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.product_category_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.product_category_id_seq OWNER TO postgres;

--
-- TOC entry 5211 (class 0 OID 0)
-- Dependencies: 225
-- Name: product_category_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.product_category_id_seq OWNED BY public.product_category.id;


--
-- TOC entry 227 (class 1259 OID 16460)
-- Dependencies: 228
-- Name: product_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.product_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.product_id_seq OWNER TO postgres;

--
-- TOC entry 5212 (class 0 OID 0)
-- Dependencies: 227
-- Name: product_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.product_id_seq OWNED BY public.product.id;


--
-- TOC entry 258 (class 1259 OID 16790)
-- Name: refresh_token; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.refresh_token (
    id integer NOT NULL,
    user_id integer NOT NULL,
    token character varying(500) NOT NULL,
    expires_at timestamp without time zone NOT NULL,
    is_revoked boolean DEFAULT false,
    revoked_at timestamp without time zone,
    replaced_by_token character varying(500),
    revoked_by_ip character varying(45),
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.refresh_token OWNER TO postgres;

--
-- TOC entry 257 (class 1259 OID 16789)
-- Dependencies: 258
-- Name: refresh_token_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.refresh_token_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.refresh_token_id_seq OWNER TO postgres;

--
-- TOC entry 5213 (class 0 OID 0)
-- Dependencies: 257
-- Name: refresh_token_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.refresh_token_id_seq OWNED BY public.refresh_token.id;


--
-- TOC entry 246 (class 1259 OID 16650)
-- Name: supplier; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public.supplier (
    id integer NOT NULL,
    name character varying(100) NOT NULL,
    phone character varying(10) NOT NULL,
    address character varying(200),
    email character varying(200),
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.supplier OWNER TO postgres;

--
-- TOC entry 245 (class 1259 OID 16649)
-- Dependencies: 246
-- Name: supplier_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.supplier_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.supplier_id_seq OWNER TO postgres;

--
-- TOC entry 5214 (class 0 OID 0)
-- Dependencies: 245
-- Name: supplier_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.supplier_id_seq OWNED BY public.supplier.id;


--
-- TOC entry 230 (class 1259 OID 16476)
-- Name: user; Type: TABLE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TABLE public."user" (
    id integer NOT NULL,
    branch_id integer NOT NULL,
    role character varying(30) NOT NULL,
    name character varying(150) NOT NULL,
    email character varying(100) NOT NULL,
    phone character varying(10) NOT NULL,
    password_hash character varying(255) NOT NULL,
    active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now(),
    CONSTRAINT user_role_check CHECK (((role)::text = ANY ((ARRAY['superadmin'::character varying, 'admin'::character varying, 'cashier'::character varying, 'kitchen'::character varying, 'deliveryman'::character varying])::text[])))
);


ALTER TABLE public."user" OWNER TO postgres;

--
-- TOC entry 229 (class 1259 OID 16475)
-- Dependencies: 230
-- Name: user_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE SEQUENCE public.user_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.user_id_seq OWNER TO postgres;

--
-- TOC entry 5215 (class 0 OID 0)
-- Dependencies: 229
-- Name: user_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER SEQUENCE public.user_id_seq OWNED BY public."user".id;


--
-- TOC entry 4811 (class 2604 OID 16431)
-- Dependencies: 224 223 224
-- Name: address id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.address ALTER COLUMN id SET DEFAULT nextval('public.address_id_seq'::regclass);


--
-- TOC entry 4829 (class 2604 OID 16512)
-- Dependencies: 234 233 234
-- Name: app id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.app ALTER COLUMN id SET DEFAULT nextval('public.app_id_seq'::regclass);


--
-- TOC entry 4848 (class 2604 OID 16613)
-- Dependencies: 241 242 242
-- Name: app_payment id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.app_payment ALTER COLUMN id SET DEFAULT nextval('public.app_payment_id_seq'::regclass);


--
-- TOC entry 4825 (class 2604 OID 16497)
-- Dependencies: 231 232 232
-- Name: bank id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.bank ALTER COLUMN id SET DEFAULT nextval('public.bank_id_seq'::regclass);


--
-- TOC entry 4852 (class 2604 OID 16633)
-- Dependencies: 243 244 244
-- Name: bank_payment id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.bank_payment ALTER COLUMN id SET DEFAULT nextval('public.bank_payment_id_seq'::regclass);


--
-- TOC entry 4801 (class 2604 OID 16393)
-- Dependencies: 217 218 218
-- Name: branch id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.branch ALTER COLUMN id SET DEFAULT nextval('public.branch_id_seq'::regclass);


--
-- TOC entry 4804 (class 2604 OID 16402)
-- Dependencies: 219 220 220
-- Name: customer id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.customer ALTER COLUMN id SET DEFAULT nextval('public.customer_id_seq'::regclass);


--
-- TOC entry 4862 (class 2604 OID 16673)
-- Dependencies: 249 250 250
-- Name: expense id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense ALTER COLUMN id SET DEFAULT nextval('public.expense_id_seq'::regclass);


--
-- TOC entry 4873 (class 2604 OID 16728)
-- Dependencies: 255 256 256
-- Name: expense_bank_payment id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_bank_payment ALTER COLUMN id SET DEFAULT nextval('public.expense_bank_payment_id_seq'::regclass);


--
-- TOC entry 4859 (class 2604 OID 16664)
-- Dependencies: 248 247 248
-- Name: expense_category id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_category ALTER COLUMN id SET DEFAULT nextval('public.expense_category_id_seq'::regclass);


--
-- TOC entry 4869 (class 2604 OID 16708)
-- Dependencies: 254 253 254
-- Name: expense_detail id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_detail ALTER COLUMN id SET DEFAULT nextval('public.expense_detail_id_seq'::regclass);


--
-- TOC entry 4866 (class 2604 OID 16689)
-- Dependencies: 251 252 252
-- Name: expense_header id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_header ALTER COLUMN id SET DEFAULT nextval('public.expense_header_id_seq'::regclass);


--
-- TOC entry 4833 (class 2604 OID 16527)
-- Dependencies: 236 235 236
-- Name: loyalty_rule id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.loyalty_rule ALTER COLUMN id SET DEFAULT nextval('public.loyalty_rule_id_seq'::regclass);


--
-- TOC entry 4808 (class 2604 OID 16417)
-- Dependencies: 222 221 222
-- Name: neighborhood id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.neighborhood ALTER COLUMN id SET DEFAULT nextval('public.neighborhood_id_seq'::regclass);


--
-- TOC entry 4836 (class 2604 OID 16543)
-- Dependencies: 238 237 238
-- Name: order id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."order" ALTER COLUMN id SET DEFAULT nextval('public.order_id_seq'::regclass);


--
-- TOC entry 4842 (class 2604 OID 16589)
-- Dependencies: 239 240 240
-- Name: order_detail id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.order_detail ALTER COLUMN id SET DEFAULT nextval('public.order_detail_id_seq'::regclass);


--
-- TOC entry 4817 (class 2604 OID 16464)
-- Dependencies: 227 228 228
-- Name: product id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.product ALTER COLUMN id SET DEFAULT nextval('public.product_id_seq'::regclass);


--
-- TOC entry 4814 (class 2604 OID 16450)
-- Dependencies: 226 225 226
-- Name: product_category id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.product_category ALTER COLUMN id SET DEFAULT nextval('public.product_category_id_seq'::regclass);


--
-- TOC entry 4876 (class 2604 OID 16793)
-- Dependencies: 257 258 258
-- Name: refresh_token id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.refresh_token ALTER COLUMN id SET DEFAULT nextval('public.refresh_token_id_seq'::regclass);


--
-- TOC entry 4856 (class 2604 OID 16653)
-- Dependencies: 246 245 246
-- Name: supplier id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.supplier ALTER COLUMN id SET DEFAULT nextval('public.supplier_id_seq'::regclass);


--
-- TOC entry 4821 (class 2604 OID 16479)
-- Dependencies: 229 230 230
-- Name: user id; Type: DEFAULT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."user" ALTER COLUMN id SET DEFAULT nextval('public.user_id_seq'::regclass);


--
-- TOC entry 5154 (class 0 OID 16428)
-- Dependencies: 224
-- Data for Name: address; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80000
--

COPY public.address (id, customer_id, neighborhood_id, address, additional_info, delivery_fee, latitude, longitude, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5164 (class 0 OID 16509)
-- Dependencies: 234
-- Data for Name: app; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80029
--

COPY public.app (id, bank_id, name, image_url, active, created_at, updated_at, deleted_at) FROM stdin;
\.


--
-- TOC entry 5172 (class 0 OID 16610)
-- Dependencies: 242
-- Data for Name: app_payment; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80058
--

COPY public.app_payment (id, order_id, app_id, amount, is_setted, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5162 (class 0 OID 16494)
-- Dependencies: 232
-- Data for Name: bank; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80087
--

COPY public.bank (id, branch_id, name, image_url, active, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5174 (class 0 OID 16630)
-- Dependencies: 244
-- Data for Name: bank_payment; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80116
--

COPY public.bank_payment (id, order_id, bank_id, amount, is_verified, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5148 (class 0 OID 16390)
-- Dependencies: 218
-- Data for Name: branch; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80145
--

COPY public.branch (id, name, address, phone1, phone2, created_at, updated_at) FROM stdin;
1	Sucursal Principal	Dirección Principal	0000000000	\N	2025-09-13 17:11:42.210097	2025-09-13 17:11:42.210097
\.


--
-- TOC entry 5150 (class 0 OID 16399)
-- Dependencies: 220
-- Data for Name: customer; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80243
--

COPY public.customer (id, branch_id, name, phone1, phone2, active, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5180 (class 0 OID 16670)
-- Dependencies: 250
-- Data for Name: expense; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80272
--

COPY public.expense (id, name, category_id, unit, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5186 (class 0 OID 16725)
-- Dependencies: 256
-- Data for Name: expense_bank_payment; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80301
--

COPY public.expense_bank_payment (id, bank_id, expense_header_id, amount, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5178 (class 0 OID 16661)
-- Dependencies: 248
-- Data for Name: expense_category; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80330
--

COPY public.expense_category (id, name, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5184 (class 0 OID 16705)
-- Dependencies: 254
-- Data for Name: expense_detail; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80359
--

COPY public.expense_detail (id, header_id, expense_id, quantity, amount, total, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5182 (class 0 OID 16686)
-- Dependencies: 252
-- Data for Name: expense_header; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80388
--

COPY public.expense_header (id, branch_id, supplier_id, total, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5166 (class 0 OID 16524)
-- Dependencies: 236
-- Data for Name: loyalty_rule; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80417
--

COPY public.loyalty_rule (id, branch_id, description, n_orders_needed, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5152 (class 0 OID 16414)
-- Dependencies: 222
-- Data for Name: neighborhood; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80446
--

COPY public.neighborhood (id, branch_id, name, delivery_fee, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5168 (class 0 OID 16540)
-- Dependencies: 238
-- Data for Name: order; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80475
--

COPY public."order" (id, branch_id, taken_by_id, customer_id, address_id, loyalty_rule_id, delivery_man_id, type, delivery_fee, reserved_for, status, status_times, subtotal, total, discount_total, notes, cancelled_reason, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5170 (class 0 OID 16586)
-- Dependencies: 240
-- Data for Name: order_detail; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80504
--

COPY public.order_detail (id, order_id, product_id, quantity, unit_price, discount, subtotal, notes, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5158 (class 0 OID 16461)
-- Dependencies: 228
-- Data for Name: product; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80533
--

COPY public.product (id, category_id, name, price, stock, active, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5156 (class 0 OID 16447)
-- Dependencies: 226
-- Data for Name: product_category; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80562
--

COPY public.product_category (id, branch_id, name, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5188 (class 0 OID 16790)
-- Dependencies: 258
-- Data for Name: refresh_token; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80591
--

COPY public.refresh_token (id, user_id, token, expires_at, is_revoked, revoked_at, replaced_by_token, revoked_by_ip, created_at, updated_at) FROM stdin;
1	1	+uIHv0We0+JsG2IGJeecrw4JL89klxOgBVK5Dadtpas7bogZnoV5GMgN18bulRuoUmcxnCf6NPJeo/cz11A6xQ==	2025-09-20 17:12:26.59063	t	2025-09-13 18:40:20.525021	\N	::1	2025-09-13 17:12:26.590644	2025-09-13 18:40:20.540881
2	1	tn5H+tSCw6dIx3nWJqYOiVIgpo6cWUaDQcVron71OCYzn1r+zDLfw3HDQFYse/eb/HNSKigxa6dSFQJNWqYRUA==	2025-09-20 18:40:20.555276	f	\N	\N	\N	2025-09-13 18:40:20.555277	2025-09-13 18:40:20.555275
\.


--
-- TOC entry 5176 (class 0 OID 16650)
-- Dependencies: 246
-- Data for Name: supplier; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80874
--

COPY public.supplier (id, name, phone, address, email, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 5160 (class 0 OID 16476)
-- Dependencies: 230
-- Data for Name: user; Type: TABLE DATA; Schema: public; Owner: postgres
-- Data Pos: 80903
--

COPY public."user" (id, branch_id, role, name, email, phone, password_hash, active, created_at, updated_at) FROM stdin;
1	1	superadmin	Santiago Velásquez	santyvano@outlook.com	3127163848	$2a$11$5ERmxaaUoW.MLkirB/l22eyaZxF1OCK2kjtN9DtYo14k.o2XmkyBy	t	2025-09-13 17:11:54.192027	2025-09-13 17:11:54.192027
\.


--
-- TOC entry 5216 (class 0 OID 0)
-- Dependencies: 223
-- Name: address_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.address_id_seq', 1, false);


--
-- TOC entry 5217 (class 0 OID 0)
-- Dependencies: 233
-- Name: app_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.app_id_seq', 1, false);


--
-- TOC entry 5218 (class 0 OID 0)
-- Dependencies: 241
-- Name: app_payment_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.app_payment_id_seq', 1, false);


--
-- TOC entry 5219 (class 0 OID 0)
-- Dependencies: 231
-- Name: bank_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.bank_id_seq', 1, false);


--
-- TOC entry 5220 (class 0 OID 0)
-- Dependencies: 243
-- Name: bank_payment_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.bank_payment_id_seq', 1, false);


--
-- TOC entry 5221 (class 0 OID 0)
-- Dependencies: 217
-- Name: branch_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.branch_id_seq', 1, true);


--
-- TOC entry 5222 (class 0 OID 0)
-- Dependencies: 219
-- Name: customer_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.customer_id_seq', 1, false);


--
-- TOC entry 5223 (class 0 OID 0)
-- Dependencies: 255
-- Name: expense_bank_payment_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.expense_bank_payment_id_seq', 1, false);


--
-- TOC entry 5224 (class 0 OID 0)
-- Dependencies: 247
-- Name: expense_category_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.expense_category_id_seq', 1, false);


--
-- TOC entry 5225 (class 0 OID 0)
-- Dependencies: 253
-- Name: expense_detail_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.expense_detail_id_seq', 1, false);


--
-- TOC entry 5226 (class 0 OID 0)
-- Dependencies: 251
-- Name: expense_header_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.expense_header_id_seq', 1, false);


--
-- TOC entry 5227 (class 0 OID 0)
-- Dependencies: 249
-- Name: expense_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.expense_id_seq', 1, false);


--
-- TOC entry 5228 (class 0 OID 0)
-- Dependencies: 235
-- Name: loyalty_rule_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.loyalty_rule_id_seq', 1, false);


--
-- TOC entry 5229 (class 0 OID 0)
-- Dependencies: 221
-- Name: neighborhood_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.neighborhood_id_seq', 1, false);


--
-- TOC entry 5230 (class 0 OID 0)
-- Dependencies: 239
-- Name: order_detail_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.order_detail_id_seq', 1, false);


--
-- TOC entry 5231 (class 0 OID 0)
-- Dependencies: 237
-- Name: order_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.order_id_seq', 1, false);


--
-- TOC entry 5232 (class 0 OID 0)
-- Dependencies: 225
-- Name: product_category_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.product_category_id_seq', 1, false);


--
-- TOC entry 5233 (class 0 OID 0)
-- Dependencies: 227
-- Name: product_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.product_id_seq', 1, false);


--
-- TOC entry 5234 (class 0 OID 0)
-- Dependencies: 257
-- Name: refresh_token_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.refresh_token_id_seq', 2, true);


--
-- TOC entry 5235 (class 0 OID 0)
-- Dependencies: 245
-- Name: supplier_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.supplier_id_seq', 1, false);


--
-- TOC entry 5236 (class 0 OID 0)
-- Dependencies: 229
-- Name: user_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
-- Data Pos: 0
--

SELECT pg_catalog.setval('public.user_id_seq', 1, true);


--
-- TOC entry 4894 (class 2606 OID 16435)
-- Dependencies: 224
-- Name: address address_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.address
    ADD CONSTRAINT address_pkey PRIMARY KEY (id);


--
-- TOC entry 4927 (class 2606 OID 16618)
-- Dependencies: 242
-- Name: app_payment app_payment_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.app_payment
    ADD CONSTRAINT app_payment_pkey PRIMARY KEY (id);


--
-- TOC entry 4911 (class 2606 OID 16517)
-- Dependencies: 234
-- Name: app app_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.app
    ADD CONSTRAINT app_pkey PRIMARY KEY (id);


--
-- TOC entry 4929 (class 2606 OID 16638)
-- Dependencies: 244
-- Name: bank_payment bank_payment_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.bank_payment
    ADD CONSTRAINT bank_payment_pkey PRIMARY KEY (id);


--
-- TOC entry 4909 (class 2606 OID 16502)
-- Dependencies: 232
-- Name: bank bank_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.bank
    ADD CONSTRAINT bank_pkey PRIMARY KEY (id);


--
-- TOC entry 4885 (class 2606 OID 16397)
-- Dependencies: 218
-- Name: branch branch_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.branch
    ADD CONSTRAINT branch_pkey PRIMARY KEY (id);


--
-- TOC entry 4887 (class 2606 OID 16407)
-- Dependencies: 220
-- Name: customer customer_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.customer
    ADD CONSTRAINT customer_pkey PRIMARY KEY (id);


--
-- TOC entry 4944 (class 2606 OID 16732)
-- Dependencies: 256
-- Name: expense_bank_payment expense_bank_payment_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_bank_payment
    ADD CONSTRAINT expense_bank_payment_pkey PRIMARY KEY (id);


--
-- TOC entry 4933 (class 2606 OID 16668)
-- Dependencies: 248
-- Name: expense_category expense_category_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_category
    ADD CONSTRAINT expense_category_pkey PRIMARY KEY (id);


--
-- TOC entry 4941 (class 2606 OID 16713)
-- Dependencies: 254
-- Name: expense_detail expense_detail_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_detail
    ADD CONSTRAINT expense_detail_pkey PRIMARY KEY (id);


--
-- TOC entry 4937 (class 2606 OID 16693)
-- Dependencies: 252
-- Name: expense_header expense_header_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_header
    ADD CONSTRAINT expense_header_pkey PRIMARY KEY (id);


--
-- TOC entry 4935 (class 2606 OID 16679)
-- Dependencies: 250
-- Name: expense expense_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense
    ADD CONSTRAINT expense_pkey PRIMARY KEY (id);


--
-- TOC entry 4913 (class 2606 OID 16533)
-- Dependencies: 236
-- Name: loyalty_rule loyalty_rule_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.loyalty_rule
    ADD CONSTRAINT loyalty_rule_pkey PRIMARY KEY (id);


--
-- TOC entry 4892 (class 2606 OID 16421)
-- Dependencies: 222
-- Name: neighborhood neighborhood_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.neighborhood
    ADD CONSTRAINT neighborhood_pkey PRIMARY KEY (id);


--
-- TOC entry 4925 (class 2606 OID 16598)
-- Dependencies: 240
-- Name: order_detail order_detail_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.order_detail
    ADD CONSTRAINT order_detail_pkey PRIMARY KEY (id);


--
-- TOC entry 4921 (class 2606 OID 16554)
-- Dependencies: 238
-- Name: order order_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."order"
    ADD CONSTRAINT order_pkey PRIMARY KEY (id);


--
-- TOC entry 4898 (class 2606 OID 16454)
-- Dependencies: 226
-- Name: product_category product_category_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.product_category
    ADD CONSTRAINT product_category_pkey PRIMARY KEY (id);


--
-- TOC entry 4902 (class 2606 OID 16469)
-- Dependencies: 228
-- Name: product product_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.product
    ADD CONSTRAINT product_pkey PRIMARY KEY (id);


--
-- TOC entry 4950 (class 2606 OID 16800)
-- Dependencies: 258
-- Name: refresh_token refresh_token_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.refresh_token
    ADD CONSTRAINT refresh_token_pkey PRIMARY KEY (id);


--
-- TOC entry 4952 (class 2606 OID 16802)
-- Dependencies: 258
-- Name: refresh_token refresh_token_token_key; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.refresh_token
    ADD CONSTRAINT refresh_token_token_key UNIQUE (token);


--
-- TOC entry 4931 (class 2606 OID 16659)
-- Dependencies: 246
-- Name: supplier supplier_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.supplier
    ADD CONSTRAINT supplier_pkey PRIMARY KEY (id);


--
-- TOC entry 4907 (class 2606 OID 16487)
-- Dependencies: 230
-- Name: user user_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."user"
    ADD CONSTRAINT user_pkey PRIMARY KEY (id);


--
-- TOC entry 4895 (class 1259 OID 16746)
-- Dependencies: 224
-- Name: idx_address_customer; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_address_customer ON public.address USING btree (customer_id);


--
-- TOC entry 4896 (class 1259 OID 16747)
-- Dependencies: 224
-- Name: idx_address_neighborhood; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_address_neighborhood ON public.address USING btree (neighborhood_id);


--
-- TOC entry 4888 (class 1259 OID 16745)
-- Dependencies: 220 220
-- Name: idx_customer_active; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_customer_active ON public.customer USING btree (active) WHERE (active = true);


--
-- TOC entry 4889 (class 1259 OID 16743)
-- Dependencies: 220
-- Name: idx_customer_branch; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_customer_branch ON public.customer USING btree (branch_id);


--
-- TOC entry 4890 (class 1259 OID 16744)
-- Dependencies: 220
-- Name: idx_customer_phone; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_customer_phone ON public.customer USING btree (phone1);


--
-- TOC entry 4942 (class 1259 OID 16763)
-- Dependencies: 254
-- Name: idx_expense_detail_header; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_expense_detail_header ON public.expense_detail USING btree (header_id);


--
-- TOC entry 4938 (class 1259 OID 16761)
-- Dependencies: 252
-- Name: idx_expense_header_branch; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_expense_header_branch ON public.expense_header USING btree (branch_id);


--
-- TOC entry 4939 (class 1259 OID 16762)
-- Dependencies: 252
-- Name: idx_expense_header_supplier; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_expense_header_supplier ON public.expense_header USING btree (supplier_id);


--
-- TOC entry 4914 (class 1259 OID 16753)
-- Dependencies: 238
-- Name: idx_order_branch; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_order_branch ON public."order" USING btree (branch_id);


--
-- TOC entry 4915 (class 1259 OID 16754)
-- Dependencies: 238
-- Name: idx_order_customer; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_order_customer ON public."order" USING btree (customer_id);


--
-- TOC entry 4916 (class 1259 OID 16757)
-- Dependencies: 238
-- Name: idx_order_date; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_order_date ON public."order" USING btree (created_at);


--
-- TOC entry 4917 (class 1259 OID 16758)
-- Dependencies: 238
-- Name: idx_order_delivery_man; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_order_delivery_man ON public."order" USING btree (delivery_man_id);


--
-- TOC entry 4922 (class 1259 OID 16759)
-- Dependencies: 240
-- Name: idx_order_detail_order; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_order_detail_order ON public.order_detail USING btree (order_id);


--
-- TOC entry 4923 (class 1259 OID 16760)
-- Dependencies: 240
-- Name: idx_order_detail_product; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_order_detail_product ON public.order_detail USING btree (product_id);


--
-- TOC entry 4918 (class 1259 OID 16755)
-- Dependencies: 238
-- Name: idx_order_status; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_order_status ON public."order" USING btree (status);


--
-- TOC entry 4919 (class 1259 OID 16756)
-- Dependencies: 238
-- Name: idx_order_type; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_order_type ON public."order" USING btree (type);


--
-- TOC entry 4899 (class 1259 OID 16749)
-- Dependencies: 228 228
-- Name: idx_product_active; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_product_active ON public.product USING btree (active) WHERE (active = true);


--
-- TOC entry 4900 (class 1259 OID 16748)
-- Dependencies: 228
-- Name: idx_product_category; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_product_category ON public.product USING btree (category_id);


--
-- TOC entry 4945 (class 1259 OID 16810)
-- Dependencies: 258
-- Name: idx_refresh_token_expires_at; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_refresh_token_expires_at ON public.refresh_token USING btree (expires_at);


--
-- TOC entry 4946 (class 1259 OID 16808)
-- Dependencies: 258
-- Name: idx_refresh_token_token; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE UNIQUE INDEX idx_refresh_token_token ON public.refresh_token USING btree (token);


--
-- TOC entry 4947 (class 1259 OID 16811)
-- Dependencies: 258 258
-- Name: idx_refresh_token_user_active; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_refresh_token_user_active ON public.refresh_token USING btree (user_id, is_revoked);


--
-- TOC entry 4948 (class 1259 OID 16809)
-- Dependencies: 258
-- Name: idx_refresh_token_user_id; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_refresh_token_user_id ON public.refresh_token USING btree (user_id);


--
-- TOC entry 4903 (class 1259 OID 16752)
-- Dependencies: 230 230
-- Name: idx_user_active; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_user_active ON public."user" USING btree (active) WHERE (active = true);


--
-- TOC entry 4904 (class 1259 OID 16750)
-- Dependencies: 230
-- Name: idx_user_branch; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_user_branch ON public."user" USING btree (branch_id);


--
-- TOC entry 4905 (class 1259 OID 16751)
-- Dependencies: 230
-- Name: idx_user_role; Type: INDEX; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE INDEX idx_user_role ON public."user" USING btree (role);


--
-- TOC entry 4997 (class 2620 OID 16777)
-- Dependencies: 261 254
-- Name: expense_detail calculate_expense_detail_total_trigger; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER calculate_expense_detail_total_trigger BEFORE INSERT OR UPDATE ON public.expense_detail FOR EACH ROW EXECUTE FUNCTION public.calculate_expense_detail_total();


--
-- TOC entry 4992 (class 2620 OID 16775)
-- Dependencies: 260 240
-- Name: order_detail calculate_order_detail_subtotal_trigger; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER calculate_order_detail_subtotal_trigger BEFORE INSERT OR UPDATE ON public.order_detail FOR EACH ROW EXECUTE FUNCTION public.calculate_order_detail_subtotal();


--
-- TOC entry 4986 (class 2620 OID 16768)
-- Dependencies: 224 259
-- Name: address update_address_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_address_updated_at BEFORE UPDATE ON public.address FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4983 (class 2620 OID 16765)
-- Dependencies: 259 218
-- Name: branch update_branch_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_branch_updated_at BEFORE UPDATE ON public.branch FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4984 (class 2620 OID 16766)
-- Dependencies: 220 259
-- Name: customer update_customer_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_customer_updated_at BEFORE UPDATE ON public.customer FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4998 (class 2620 OID 16785)
-- Dependencies: 263 254
-- Name: expense_detail update_expense_header_total_on_detail_delete; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_expense_header_total_on_detail_delete AFTER DELETE ON public.expense_detail FOR EACH ROW EXECUTE FUNCTION public.update_expense_header_total();


--
-- TOC entry 4999 (class 2620 OID 16783)
-- Dependencies: 254 263
-- Name: expense_detail update_expense_header_total_on_detail_insert; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_expense_header_total_on_detail_insert AFTER INSERT ON public.expense_detail FOR EACH ROW EXECUTE FUNCTION public.update_expense_header_total();


--
-- TOC entry 5000 (class 2620 OID 16784)
-- Dependencies: 263 254
-- Name: expense_detail update_expense_header_total_on_detail_update; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_expense_header_total_on_detail_update AFTER UPDATE ON public.expense_detail FOR EACH ROW EXECUTE FUNCTION public.update_expense_header_total();


--
-- TOC entry 4985 (class 2620 OID 16767)
-- Dependencies: 222 259
-- Name: neighborhood update_neighborhood_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_neighborhood_updated_at BEFORE UPDATE ON public.neighborhood FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4993 (class 2620 OID 16773)
-- Dependencies: 240 259
-- Name: order_detail update_order_detail_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_order_detail_updated_at BEFORE UPDATE ON public.order_detail FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4990 (class 2620 OID 16787)
-- Dependencies: 238 264
-- Name: order update_order_total_on_delivery_fee_change_trigger; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_order_total_on_delivery_fee_change_trigger BEFORE UPDATE ON public."order" FOR EACH ROW EXECUTE FUNCTION public.update_order_total_on_delivery_fee_change();


--
-- TOC entry 4994 (class 2620 OID 16781)
-- Dependencies: 262 240
-- Name: order_detail update_order_totals_on_detail_delete; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_order_totals_on_detail_delete AFTER DELETE ON public.order_detail FOR EACH ROW EXECUTE FUNCTION public.update_order_totals();


--
-- TOC entry 4995 (class 2620 OID 16779)
-- Dependencies: 262 240
-- Name: order_detail update_order_totals_on_detail_insert; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_order_totals_on_detail_insert AFTER INSERT ON public.order_detail FOR EACH ROW EXECUTE FUNCTION public.update_order_totals();


--
-- TOC entry 4996 (class 2620 OID 16780)
-- Dependencies: 262 240
-- Name: order_detail update_order_totals_on_detail_update; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_order_totals_on_detail_update AFTER UPDATE ON public.order_detail FOR EACH ROW EXECUTE FUNCTION public.update_order_totals();


--
-- TOC entry 4991 (class 2620 OID 16772)
-- Dependencies: 259 238
-- Name: order update_order_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_order_updated_at BEFORE UPDATE ON public."order" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4987 (class 2620 OID 16769)
-- Dependencies: 259 226
-- Name: product_category update_product_category_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_product_category_updated_at BEFORE UPDATE ON public.product_category FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4988 (class 2620 OID 16770)
-- Dependencies: 259 228
-- Name: product update_product_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_product_updated_at BEFORE UPDATE ON public.product FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 5001 (class 2620 OID 16812)
-- Dependencies: 259 258
-- Name: refresh_token update_refresh_token_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_refresh_token_updated_at BEFORE UPDATE ON public.refresh_token FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4989 (class 2620 OID 16771)
-- Dependencies: 259 230
-- Name: user update_user_updated_at; Type: TRIGGER; Schema: public; Owner: postgres
-- Data Pos: 0
--

CREATE TRIGGER update_user_updated_at BEFORE UPDATE ON public."user" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4955 (class 2606 OID 16436)
-- Dependencies: 220 4887 224
-- Name: address address_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.address
    ADD CONSTRAINT address_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customer(id);


--
-- TOC entry 4956 (class 2606 OID 16441)
-- Dependencies: 224 4892 222
-- Name: address address_neighborhood_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.address
    ADD CONSTRAINT address_neighborhood_id_fkey FOREIGN KEY (neighborhood_id) REFERENCES public.neighborhood(id);


--
-- TOC entry 4961 (class 2606 OID 16518)
-- Dependencies: 4909 234 232
-- Name: app app_bank_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.app
    ADD CONSTRAINT app_bank_id_fkey FOREIGN KEY (bank_id) REFERENCES public.bank(id);


--
-- TOC entry 4971 (class 2606 OID 16624)
-- Dependencies: 242 234 4911
-- Name: app_payment app_payment_app_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.app_payment
    ADD CONSTRAINT app_payment_app_id_fkey FOREIGN KEY (app_id) REFERENCES public.app(id);


--
-- TOC entry 4972 (class 2606 OID 16619)
-- Dependencies: 242 238 4921
-- Name: app_payment app_payment_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.app_payment
    ADD CONSTRAINT app_payment_order_id_fkey FOREIGN KEY (order_id) REFERENCES public."order"(id);


--
-- TOC entry 4960 (class 2606 OID 16503)
-- Dependencies: 232 4885 218
-- Name: bank bank_branch_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.bank
    ADD CONSTRAINT bank_branch_id_fkey FOREIGN KEY (branch_id) REFERENCES public.branch(id);


--
-- TOC entry 4973 (class 2606 OID 16644)
-- Dependencies: 4909 232 244
-- Name: bank_payment bank_payment_bank_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.bank_payment
    ADD CONSTRAINT bank_payment_bank_id_fkey FOREIGN KEY (bank_id) REFERENCES public.bank(id);


--
-- TOC entry 4974 (class 2606 OID 16639)
-- Dependencies: 238 244 4921
-- Name: bank_payment bank_payment_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.bank_payment
    ADD CONSTRAINT bank_payment_order_id_fkey FOREIGN KEY (order_id) REFERENCES public."order"(id);


--
-- TOC entry 4953 (class 2606 OID 16408)
-- Dependencies: 220 4885 218
-- Name: customer customer_branch_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.customer
    ADD CONSTRAINT customer_branch_id_fkey FOREIGN KEY (branch_id) REFERENCES public.branch(id);


--
-- TOC entry 4980 (class 2606 OID 16733)
-- Dependencies: 232 4909 256
-- Name: expense_bank_payment expense_bank_payment_bank_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_bank_payment
    ADD CONSTRAINT expense_bank_payment_bank_id_fkey FOREIGN KEY (bank_id) REFERENCES public.bank(id);


--
-- TOC entry 4981 (class 2606 OID 16738)
-- Dependencies: 4937 252 256
-- Name: expense_bank_payment expense_bank_payment_expense_header_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_bank_payment
    ADD CONSTRAINT expense_bank_payment_expense_header_id_fkey FOREIGN KEY (expense_header_id) REFERENCES public.expense_header(id);


--
-- TOC entry 4975 (class 2606 OID 16680)
-- Dependencies: 248 250 4933
-- Name: expense expense_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense
    ADD CONSTRAINT expense_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.expense_category(id);


--
-- TOC entry 4978 (class 2606 OID 16719)
-- Dependencies: 4935 254 250
-- Name: expense_detail expense_detail_expense_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_detail
    ADD CONSTRAINT expense_detail_expense_id_fkey FOREIGN KEY (expense_id) REFERENCES public.expense(id);


--
-- TOC entry 4979 (class 2606 OID 16714)
-- Dependencies: 254 252 4937
-- Name: expense_detail expense_detail_header_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_detail
    ADD CONSTRAINT expense_detail_header_id_fkey FOREIGN KEY (header_id) REFERENCES public.expense_header(id);


--
-- TOC entry 4976 (class 2606 OID 16694)
-- Dependencies: 218 4885 252
-- Name: expense_header expense_header_branch_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_header
    ADD CONSTRAINT expense_header_branch_id_fkey FOREIGN KEY (branch_id) REFERENCES public.branch(id);


--
-- TOC entry 4977 (class 2606 OID 16699)
-- Dependencies: 252 246 4931
-- Name: expense_header expense_header_supplier_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.expense_header
    ADD CONSTRAINT expense_header_supplier_id_fkey FOREIGN KEY (supplier_id) REFERENCES public.supplier(id);


--
-- TOC entry 4962 (class 2606 OID 16534)
-- Dependencies: 218 236 4885
-- Name: loyalty_rule loyalty_rule_branch_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.loyalty_rule
    ADD CONSTRAINT loyalty_rule_branch_id_fkey FOREIGN KEY (branch_id) REFERENCES public.branch(id);


--
-- TOC entry 4954 (class 2606 OID 16422)
-- Dependencies: 218 4885 222
-- Name: neighborhood neighborhood_branch_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.neighborhood
    ADD CONSTRAINT neighborhood_branch_id_fkey FOREIGN KEY (branch_id) REFERENCES public.branch(id);


--
-- TOC entry 4963 (class 2606 OID 16570)
-- Dependencies: 4894 238 224
-- Name: order order_address_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."order"
    ADD CONSTRAINT order_address_id_fkey FOREIGN KEY (address_id) REFERENCES public.address(id);


--
-- TOC entry 4964 (class 2606 OID 16555)
-- Dependencies: 218 4885 238
-- Name: order order_branch_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."order"
    ADD CONSTRAINT order_branch_id_fkey FOREIGN KEY (branch_id) REFERENCES public.branch(id);


--
-- TOC entry 4965 (class 2606 OID 16565)
-- Dependencies: 220 238 4887
-- Name: order order_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."order"
    ADD CONSTRAINT order_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customer(id);


--
-- TOC entry 4966 (class 2606 OID 16580)
-- Dependencies: 4907 230 238
-- Name: order order_delivery_man_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."order"
    ADD CONSTRAINT order_delivery_man_id_fkey FOREIGN KEY (delivery_man_id) REFERENCES public."user"(id);


--
-- TOC entry 4969 (class 2606 OID 16599)
-- Dependencies: 240 4921 238
-- Name: order_detail order_detail_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.order_detail
    ADD CONSTRAINT order_detail_order_id_fkey FOREIGN KEY (order_id) REFERENCES public."order"(id);


--
-- TOC entry 4970 (class 2606 OID 16604)
-- Dependencies: 4902 240 228
-- Name: order_detail order_detail_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.order_detail
    ADD CONSTRAINT order_detail_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.product(id);


--
-- TOC entry 4967 (class 2606 OID 16575)
-- Dependencies: 4913 238 236
-- Name: order order_loyalty_rule_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."order"
    ADD CONSTRAINT order_loyalty_rule_id_fkey FOREIGN KEY (loyalty_rule_id) REFERENCES public.loyalty_rule(id);


--
-- TOC entry 4968 (class 2606 OID 16560)
-- Dependencies: 238 4907 230
-- Name: order order_taken_by_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."order"
    ADD CONSTRAINT order_taken_by_id_fkey FOREIGN KEY (taken_by_id) REFERENCES public."user"(id);


--
-- TOC entry 4957 (class 2606 OID 16455)
-- Dependencies: 4885 218 226
-- Name: product_category product_category_branch_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.product_category
    ADD CONSTRAINT product_category_branch_id_fkey FOREIGN KEY (branch_id) REFERENCES public.branch(id);


--
-- TOC entry 4958 (class 2606 OID 16470)
-- Dependencies: 4898 226 228
-- Name: product product_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.product
    ADD CONSTRAINT product_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.product_category(id);


--
-- TOC entry 4982 (class 2606 OID 16803)
-- Dependencies: 258 4907 230
-- Name: refresh_token refresh_token_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public.refresh_token
    ADD CONSTRAINT refresh_token_user_id_fkey FOREIGN KEY (user_id) REFERENCES public."user"(id) ON DELETE CASCADE;


--
-- TOC entry 4959 (class 2606 OID 16488)
-- Dependencies: 4885 230 218
-- Name: user user_branch_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
-- Data Pos: 0
--

ALTER TABLE ONLY public."user"
    ADD CONSTRAINT user_branch_id_fkey FOREIGN KEY (branch_id) REFERENCES public.branch(id);


-- Completed on 2025-11-13 23:37:55

--
-- PostgreSQL database dump complete
--

\unrestrict ySA2eBkqMXIw6DrmhgCwARPfNcxkgH15eB7p8rKo1XSKuQI7OITqkscOfsw8zqg

