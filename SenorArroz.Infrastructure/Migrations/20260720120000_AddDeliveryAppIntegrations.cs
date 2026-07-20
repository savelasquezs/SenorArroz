using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SenorArroz.Infrastructure.Data;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720120000_AddDeliveryAppIntegrations")]
public partial class AddDeliveryAppIntegrations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE IF NOT EXISTS delivery_app_connection (
            id serial PRIMARY KEY,
            branch_id integer NOT NULL REFERENCES branch(id) ON DELETE CASCADE,
            provider varchar(40) NOT NULL,
            environment varchar(20) NOT NULL DEFAULT 'sandbox',
            display_name varchar(100) NOT NULL,
            client_id varchar(255) NOT NULL,
            encrypted_client_secret text NOT NULL,
            external_store_id varchar(120) NOT NULL,
            financial_app_id integer NOT NULL REFERENCES app(id) ON DELETE RESTRICT,
            default_cooking_time_minutes integer NOT NULL DEFAULT 30,
            encrypted_webhook_secret text NOT NULL DEFAULT '',
            webhook_configured boolean NOT NULL DEFAULT false,
            is_active boolean NOT NULL DEFAULT false,
            is_verified boolean NOT NULL DEFAULT false,
            last_verified_at timestamptz NULL,
            last_catalog_sync_at timestamptz NULL,
            last_error varchar(1000) NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT ux_delivery_app_connection_branch_provider UNIQUE(branch_id, provider)
        );

        CREATE TABLE IF NOT EXISTS delivery_app_product_mapping (
            id serial PRIMARY KEY,
            connection_id integer NOT NULL REFERENCES delivery_app_connection(id) ON DELETE CASCADE,
            external_product_id varchar(160) NOT NULL,
            external_sku varchar(160) NOT NULL DEFAULT '',
            external_name varchar(300) NOT NULL DEFAULT '',
            item_type varchar(30) NOT NULL DEFAULT 'product',
            is_active boolean NOT NULL DEFAULT true,
            product_id integer NULL REFERENCES product(id) ON DELETE SET NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT ux_delivery_app_mapping_external UNIQUE(connection_id, external_product_id, item_type)
        );

        CREATE TABLE IF NOT EXISTS external_delivery_order (
            id serial PRIMARY KEY,
            connection_id integer NOT NULL REFERENCES delivery_app_connection(id) ON DELETE CASCADE,
            branch_id integer NOT NULL REFERENCES branch(id) ON DELETE CASCADE,
            external_order_id varchar(160) NOT NULL,
            external_store_id varchar(120) NOT NULL,
            status varchar(40) NOT NULL,
            customer_name varchar(200) NOT NULL DEFAULT '',
            customer_phone varchar(50) NULL,
            delivery_address varchar(600) NULL,
            delivery_method varchar(40) NOT NULL DEFAULT '',
            payment_method varchar(60) NOT NULL DEFAULT '',
            total integer NOT NULL DEFAULT 0,
            cooking_time_minutes integer NOT NULL DEFAULT 30,
            raw_payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
            lines_json jsonb NOT NULL DEFAULT '[]'::jsonb,
            internal_order_id integer NULL REFERENCES "order"(id) ON DELETE SET NULL,
            accepted_by_user_id integer NULL REFERENCES "user"(id) ON DELETE SET NULL,
            accepted_at timestamptz NULL,
            last_error varchar(1000) NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT ux_external_delivery_order_provider_id UNIQUE(connection_id, external_order_id)
        );

        CREATE TABLE IF NOT EXISTS integration_webhook_event (
            id serial PRIMARY KEY,
            connection_id integer NOT NULL REFERENCES delivery_app_connection(id) ON DELETE CASCADE,
            provider varchar(40) NOT NULL,
            event_key varchar(200) NOT NULL,
            event_type varchar(80) NOT NULL,
            payload_json jsonb NOT NULL,
            status varchar(40) NOT NULL DEFAULT 'received',
            attempt_count integer NOT NULL DEFAULT 0,
            last_error varchar(1000) NULL,
            processed_at timestamptz NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT ux_integration_webhook_event_key UNIQUE(connection_id, event_key)
        );

        ALTER TABLE "order" ADD COLUMN IF NOT EXISTS delivery_app_connection_id integer NULL REFERENCES delivery_app_connection(id) ON DELETE SET NULL;
        ALTER TABLE "order" ADD COLUMN IF NOT EXISTS external_order_id varchar(160) NULL;
        ALTER TABLE "order" ADD COLUMN IF NOT EXISTS order_source varchar(40) NULL;
        ALTER TABLE "order" ADD COLUMN IF NOT EXISTS external_fulfillment_provider varchar(40) NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_order_external_source ON "order"(delivery_app_connection_id, external_order_id) WHERE external_order_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_external_delivery_order_branch_status ON external_delivery_order(branch_id, status);
        CREATE INDEX IF NOT EXISTS idx_delivery_app_mapping_connection ON delivery_app_product_mapping(connection_id);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX IF EXISTS ux_order_external_source;
        ALTER TABLE "order" DROP COLUMN IF EXISTS external_fulfillment_provider;
        ALTER TABLE "order" DROP COLUMN IF EXISTS order_source;
        ALTER TABLE "order" DROP COLUMN IF EXISTS external_order_id;
        ALTER TABLE "order" DROP COLUMN IF EXISTS delivery_app_connection_id;
        DROP TABLE IF EXISTS integration_webhook_event;
        DROP TABLE IF EXISTS external_delivery_order;
        DROP TABLE IF EXISTS delivery_app_product_mapping;
        DROP TABLE IF EXISTS delivery_app_connection;
        """);
}
