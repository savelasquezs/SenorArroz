
BEGIN;
LOCK TABLE public.delivery_route IN SHARE ROW EXCLUSIVE MODE;
CREATE TEMP TABLE delivery_legacy_closures ON COMMIT DROP AS
 SELECT r.* FROM public.delivery_route r
 WHERE r.status IN ('open','in_progress') AND r.completed_at_utc IS NULL
 AND EXISTS (SELECT 1 FROM public.delivery_route newer
   WHERE newer.tenant_id=r.tenant_id AND newer.deliveryman_id=r.deliveryman_id
     AND newer.status IN ('open','in_progress') AND (newer.created_at,newer.id)>(r.created_at,r.id))
 AND NOT EXISTS (SELECT 1 FROM public."order" o WHERE o.delivery_route_id=r.id
   AND (o.tenant_id<>r.tenant_id OR o.delivery_man_id IS DISTINCT FROM r.deliveryman_id
     OR o.branch_id<>r.branch_id OR o.status NOT IN ('delivered','cancelled')));
INSERT INTO public.entity_audit_log(tenant_id,branch_id,entity_type,entity_id,operation_type,
 business_date,changed_at,summary_text,money_delta_json,before_json,metadata_json)
 SELECT r.tenant_id,r.branch_id,'delivery_route',r.id,'legacy_reconciliation',
 (now() AT TIME ZONE 'America/Bogota')::date,now(),
 'Cierre administrativo de ruta histórica sustituida, sin regreso físico conocido','{}'::jsonb,
 to_jsonb(r),jsonb_build_object('source','legacy','physicalReturnKnown',false)
 FROM delivery_legacy_closures r
 WHERE NOT EXISTS (SELECT 1 FROM public.entity_audit_log a WHERE a.tenant_id=r.tenant_id
   AND a.entity_type='delivery_route' AND a.entity_id=r.id AND a.operation_type='legacy_reconciliation');
UPDATE public.delivery_route r SET status=CASE WHEN EXISTS
 (SELECT 1 FROM public."order" o WHERE o.delivery_route_id=r.id AND o.status='delivered')
 THEN 'completed' ELSE 'cancelled' END,
 closure_source='legacy',completed_at_utc=NULL,actual_duration_seconds=NULL,met_sla=NULL
 FROM delivery_legacy_closures legacy WHERE legacy.id=r.id AND legacy.tenant_id=r.tenant_id;
DO $$ BEGIN
 IF EXISTS (SELECT 1 FROM public.delivery_route WHERE status IN ('open','in_progress')
   GROUP BY tenant_id,deliveryman_id HAVING count(*)>1) THEN
  RAISE EXCEPTION 'Resolve duplicate active delivery routes with an audited preflight before enabling uniqueness';
 END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_delivery_route_active_owner ON public.delivery_route(tenant_id,deliveryman_id)
 WHERE status IN ('open','in_progress');
COMMIT;
