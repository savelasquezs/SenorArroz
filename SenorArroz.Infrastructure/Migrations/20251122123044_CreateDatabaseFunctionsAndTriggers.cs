using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreateDatabaseFunctionsAndTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ==============================================================================
            // FUNCIONES
            // ==============================================================================

            // Función 1: Calcular total de expense_detail
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.calculate_expense_detail_total() RETURNS trigger
                    LANGUAGE plpgsql
                    AS $$
                BEGIN
                    NEW.total = NEW.quantity * NEW.amount;
                    RETURN NEW;
                END;
                $$;
            ");

            // Función 2: Calcular subtotal de order_detail
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.calculate_order_detail_subtotal() RETURNS trigger
                    LANGUAGE plpgsql
                    AS $$
                BEGIN
                    NEW.subtotal = (NEW.quantity * NEW.unit_price) - COALESCE(NEW.discount, 0);
                    RETURN NEW;
                END;
                $$;
            ");

            // Función 3: Actualizar total de expense_header
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.update_expense_header_total() RETURNS trigger
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
            ");

            // Función 4: Actualizar total de order cuando cambia delivery_fee
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.update_order_total_on_delivery_fee_change() RETURNS trigger
                    LANGUAGE plpgsql
                    AS $$
                BEGIN
                    IF OLD.delivery_fee IS DISTINCT FROM NEW.delivery_fee THEN
                        NEW.total = NEW.subtotal + COALESCE(NEW.delivery_fee, 0) - NEW.discount_total;
                    END IF;
                    RETURN NEW;
                END;
                $$;
            ");

            // Función 5: Actualizar totales de order cuando cambian order_detail
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.update_order_totals() RETURNS trigger
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
                        FROM ""order""
                        WHERE id = target_order_id;
                        
                        UPDATE ""order"" 
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
            ");

            // Función 6: Actualizar updated_at automáticamente
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.update_updated_at_column() RETURNS trigger
                    LANGUAGE plpgsql
                    AS $$
                BEGIN
                    NEW.updated_at = NOW();
                    RETURN NEW;
                END;
                $$;
            ");

            // Función 7: Actualizar verified_at en bank_payment
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.update_bank_payment_verified_at() RETURNS trigger
                    LANGUAGE plpgsql
                    AS $$
                BEGIN
                    -- Cuando se verifica el pago (is_verified cambia de false a true)
                    IF NEW.is_verified = true AND (OLD.is_verified = false OR OLD.is_verified IS NULL) THEN
                        NEW.verified_at := NOW();
                    -- Cuando se desverifica el pago (is_verified cambia de true a false)
                    ELSIF NEW.is_verified = false AND OLD.is_verified = true THEN
                        NEW.verified_at := NULL;
                    END IF;
                    
                    RETURN NEW;
                END;
                $$;
            ");

            // ==============================================================================
            // TRIGGERS
            // ==============================================================================

            // Triggers para expense_detail
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS calculate_expense_detail_total_trigger ON public.expense_detail;
                CREATE TRIGGER calculate_expense_detail_total_trigger 
                    BEFORE INSERT OR UPDATE ON public.expense_detail 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.calculate_expense_detail_total();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_expense_header_total_on_detail_insert ON public.expense_detail;
                CREATE TRIGGER update_expense_header_total_on_detail_insert 
                    AFTER INSERT ON public.expense_detail 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_expense_header_total();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_expense_header_total_on_detail_update ON public.expense_detail;
                CREATE TRIGGER update_expense_header_total_on_detail_update 
                    AFTER UPDATE ON public.expense_detail 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_expense_header_total();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_expense_header_total_on_detail_delete ON public.expense_detail;
                CREATE TRIGGER update_expense_header_total_on_detail_delete 
                    AFTER DELETE ON public.expense_detail 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_expense_header_total();
            ");

            // Triggers para order_detail
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS calculate_order_detail_subtotal_trigger ON public.order_detail;
                CREATE TRIGGER calculate_order_detail_subtotal_trigger 
                    BEFORE INSERT OR UPDATE ON public.order_detail 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.calculate_order_detail_subtotal();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_order_totals_on_detail_insert ON public.order_detail;
                CREATE TRIGGER update_order_totals_on_detail_insert 
                    AFTER INSERT ON public.order_detail 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_order_totals();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_order_totals_on_detail_update ON public.order_detail;
                CREATE TRIGGER update_order_totals_on_detail_update 
                    AFTER UPDATE ON public.order_detail 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_order_totals();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_order_totals_on_detail_delete ON public.order_detail;
                CREATE TRIGGER update_order_totals_on_detail_delete 
                    AFTER DELETE ON public.order_detail 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_order_totals();
            ");

            // Triggers para order
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_order_total_on_delivery_fee_change_trigger ON public.""order"";
                CREATE TRIGGER update_order_total_on_delivery_fee_change_trigger 
                    BEFORE UPDATE ON public.""order"" 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_order_total_on_delivery_fee_change();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_order_updated_at ON public.""order"";
                CREATE TRIGGER update_order_updated_at 
                    BEFORE UPDATE ON public.""order"" 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            // Triggers para bank_payment
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trigger_bank_payment_verified_at ON public.bank_payment;
                CREATE TRIGGER trigger_bank_payment_verified_at 
                    BEFORE UPDATE ON public.bank_payment 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_bank_payment_verified_at();
            ");

            // Triggers para updated_at en todas las tablas
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_address_updated_at ON public.address;
                CREATE TRIGGER update_address_updated_at 
                    BEFORE UPDATE ON public.address 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_branch_updated_at ON public.branch;
                CREATE TRIGGER update_branch_updated_at 
                    BEFORE UPDATE ON public.branch 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_customer_updated_at ON public.customer;
                CREATE TRIGGER update_customer_updated_at 
                    BEFORE UPDATE ON public.customer 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_neighborhood_updated_at ON public.neighborhood;
                CREATE TRIGGER update_neighborhood_updated_at 
                    BEFORE UPDATE ON public.neighborhood 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_order_detail_updated_at ON public.order_detail;
                CREATE TRIGGER update_order_detail_updated_at 
                    BEFORE UPDATE ON public.order_detail 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_password_reset_token_updated_at ON public.password_reset_token;
                CREATE TRIGGER update_password_reset_token_updated_at 
                    BEFORE UPDATE ON public.password_reset_token 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_product_category_updated_at ON public.product_category;
                CREATE TRIGGER update_product_category_updated_at 
                    BEFORE UPDATE ON public.product_category 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_product_updated_at ON public.product;
                CREATE TRIGGER update_product_updated_at 
                    BEFORE UPDATE ON public.product 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_refresh_token_updated_at ON public.refresh_token;
                CREATE TRIGGER update_refresh_token_updated_at 
                    BEFORE UPDATE ON public.refresh_token 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS update_user_updated_at ON public.""user"";
                CREATE TRIGGER update_user_updated_at 
                    BEFORE UPDATE ON public.""user"" 
                    FOR EACH ROW 
                    EXECUTE FUNCTION public.update_updated_at_column();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar triggers
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS calculate_expense_detail_total_trigger ON public.expense_detail;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_expense_header_total_on_detail_insert ON public.expense_detail;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_expense_header_total_on_detail_update ON public.expense_detail;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_expense_header_total_on_detail_delete ON public.expense_detail;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS calculate_order_detail_subtotal_trigger ON public.order_detail;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_order_totals_on_detail_insert ON public.order_detail;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_order_totals_on_detail_update ON public.order_detail;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_order_totals_on_detail_delete ON public.order_detail;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_order_total_on_delivery_fee_change_trigger ON public.\"order\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_order_updated_at ON public.\"order\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trigger_bank_payment_verified_at ON public.bank_payment;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_address_updated_at ON public.address;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_branch_updated_at ON public.branch;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_customer_updated_at ON public.customer;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_neighborhood_updated_at ON public.neighborhood;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_order_detail_updated_at ON public.order_detail;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_password_reset_token_updated_at ON public.password_reset_token;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_product_category_updated_at ON public.product_category;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_product_updated_at ON public.product;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_refresh_token_updated_at ON public.refresh_token;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS update_user_updated_at ON public.\"user\";");

            // Eliminar funciones
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.calculate_expense_detail_total();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.calculate_order_detail_subtotal();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.update_expense_header_total();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.update_order_total_on_delivery_fee_change();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.update_order_totals();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.update_updated_at_column();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.update_bank_payment_verified_at();");
        }
    }
}
