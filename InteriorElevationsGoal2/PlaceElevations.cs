/*
LM2.Revit.InteriorElevationsGoal2 places ElevationMarkers and associated ViewSections for each placed room in your project.
Copyright(C) 2019  Lisa-Marie Mueller

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program. If not, see<https://www.gnu.org/licenses/>
*/

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LM2.Revit
{
    [Transaction(TransactionMode.Manual)]
    public class InteriorElevationsGoal2 : IExternalCommand
    {
        private static Func<Curve, string> serializeCurve = c => "[" + c.GetEndPoint(0) + "," + c.GetEndPoint(1) + "]";
        private static Func<IEnumerable<Curve>, string> serializeCurves = cs => String.Join(",", cs.Select(serializeCurve));

        private Application application;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument UIdoc = commandData.Application.ActiveUIDocument;
            Document doc = UIdoc.Document;

            this.application = commandData.Application.Application;

            try
            {
                List<Room> allRooms = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .Select(e => e as Room)
                    .Where(e => e != null)
                    .ToList();

                List<ViewPlan> intElevPlans = DocumentElevPlanViews(doc);
                View intElevTemplate = GetViewtemplate(doc);
                foreach (Room r in allRooms)
                {
                    XYZ roomCenter = Utility.GetCenter(r);
                    if (roomCenter == null)
                    {
                        continue;
                    }
                    ViewPlan intElevPlanOfRoom = GetViewPlanOfRoom(doc, intElevPlans, r);
                    if (intElevPlanOfRoom == null)
                    {
                        continue;
                    }
                    using (Transaction tx = new Transaction(doc))
                    {
                        tx.Start("Place Elevations");

                        List<ViewSection> elevations = PlaceElevations(doc, roomCenter, intElevPlanOfRoom);
                        foreach (ViewSection elevation in elevations)
                        {
                            AssignViewTemplate(intElevTemplate, elevation);
                            BoundingBoxXYZ roombb = SetCropBox(elevation, r);
                            CreateFilledRegion(doc, elevation, roombb);
                        }

                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        public List<CurveLoop> FilledRegionBoundary(ViewSection intElev)
        {
            BoundingBoxXYZ iecb = intElev.CropBox;

            XYZ cbboundsmin = iecb.get_Bounds(0);
            XYZ cbboundsmax = iecb.get_Bounds(1);

            double[][] transform = Matrix.transform2matrix(iecb.Transform);
            double[][] inverse = Matrix.invert(transform);

            //inside boundary

            XYZ roombb1 = new XYZ(
                cbboundsmin.X + 1,
                cbboundsmin.Y + 1,
                cbboundsmin.Z
                );
            XYZ roombb2 = new XYZ(
                cbboundsmin.X + 1,
                cbboundsmax.Y - 1,
                cbboundsmin.Z
                );
            XYZ roombb3 = new XYZ(
                cbboundsmax.X - 1,
                cbboundsmax.Y - 1,
                cbboundsmin.Z
                );
            XYZ roombb4 = new XYZ(
                cbboundsmax.X - 1,
                cbboundsmin.Y + 1,
                cbboundsmin.Z
                );

            XYZ roombb1T = Matrix.matrix2xyz(Matrix.dot(inverse, Matrix.xyz2matrix(roombb1)));
            XYZ roombb2T = Matrix.matrix2xyz(Matrix.dot(inverse, Matrix.xyz2matrix(roombb2)));
            XYZ roombb3T = Matrix.matrix2xyz(Matrix.dot(inverse, Matrix.xyz2matrix(roombb3)));
            XYZ roombb4T = Matrix.matrix2xyz(Matrix.dot(inverse, Matrix.xyz2matrix(roombb4)));

            Line li0 = Line.CreateBound(roombb4T, roombb1T);
            Line li1 = Line.CreateBound(roombb1T, roombb2T);
            Line li2 = Line.CreateBound(roombb2T, roombb3T);
            Line li3 = Line.CreateBound(roombb3T, roombb4T);

            IList<Curve> insidecurves = new List<Curve>()
            {
                li0,
                li1,
                li2,
                li3
            };

            XYZ roombbout1 = new XYZ(
                cbboundsmin.X - 0.5,
                cbboundsmin.Y - 0.5,
                cbboundsmin.Z
                );
            XYZ roombbout2 = new XYZ(
                cbboundsmin.X - 0.5,
                cbboundsmax.Y + 0.5,
                cbboundsmin.Z
                );
            XYZ roombbout3 = new XYZ(
                cbboundsmax.X + 0.5,
                cbboundsmax.Y + 0.5,
                cbboundsmin.Z
                );
            XYZ roombbout4 = new XYZ(
                cbboundsmax.X + 0.5,
                cbboundsmin.Y - 0.5,
                cbboundsmin.Z
                );

            XYZ roombbout1T = Matrix.matrix2xyz(Matrix.dot(inverse, Matrix.xyz2matrix(roombbout1)));
            XYZ roombbout2T = Matrix.matrix2xyz(Matrix.dot(inverse, Matrix.xyz2matrix(roombbout2)));
            XYZ roombbout3T = Matrix.matrix2xyz(Matrix.dot(inverse, Matrix.xyz2matrix(roombbout3)));
            XYZ roombbout4T = Matrix.matrix2xyz(Matrix.dot(inverse, Matrix.xyz2matrix(roombbout4)));

            Line lo0 = Line.CreateBound(roombbout4T, roombbout1T);
            Line lo1 = Line.CreateBound(roombbout1T, roombbout2T);
            Line lo2 = Line.CreateBound(roombbout2T, roombbout3T);
            Line lo3 = Line.CreateBound(roombbout3T, roombbout4T);

            IList<Curve> outsidecurves = new List<Curve>()
            {
                lo0,
                lo1,
                lo2,
                lo3
            };

            //boundaries
            CurveLoop inside = CurveLoop.Create(insidecurves);
            CurveLoop outside = CurveLoop.Create(outsidecurves);
            List<CurveLoop> frBoundaries = new List<CurveLoop>(){
                inside,
                outside
            };

            return frBoundaries;
        }

        public void CreateFilledRegion(Document doc, ViewSection intElev, BoundingBoxXYZ roombb)
        {
            List<CurveLoop> filledRegionBoundaries = FilledRegionBoundary(intElev);
            ElementId filledRegionTypeId = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Select(f => f as FilledRegionType)
                .Where(f => f != null
                    && f.BackgroundPatternColor.Red == 255
                    && f.BackgroundPatternColor.Blue == 255
                    && f.BackgroundPatternColor.Green == 255
                    && f.IsMasking == true)
                .Select(f => f.Id)
                .FirstOrDefault();
            if (filledRegionTypeId != null)
            {
                FilledRegion region = FilledRegion.Create(doc, filledRegionTypeId, intElev.Id, filledRegionBoundaries);
                // set lineweight

                Element lineStyle = FilledRegion.GetValidLineStyleIdsForFilledRegion(doc)
                    .Select(id => doc.GetElement(id))
                    .FirstOrDefault(el => el.Name.Contains("05") && el.Name.ToLower().Contains("实线"));
                if (lineStyle != null)
                {
                    region.SetLineStyleId(lineStyle.Id);
                }
            }
        }

        //From https://lm2.me/post/2019/11/15/resizingcropboxes
        //<image url="$(ProjectDir)\DocumentImages\ElevationViewCoordinateSystems.png" scale="0.5"/>
        /// <summary>
        /// 调整裁剪框
        /// </summary>
        /// <param name="elevationView">立面视图</param>
        /// <param name="r">房间</param>
        /// <returns></returns>
        public BoundingBoxXYZ SetCropBox(ViewSection elevationView, Room r)
        {
            //立面原始裁剪框范围
            BoundingBoxXYZ evBb = elevationView.CropBox;
            XYZ cbBoundsMin = evBb.get_Bounds(0);
            XYZ cbBoundsMax = evBb.get_Bounds(1);

            //房间范围
            BoundingBoxXYZ rbb = r.get_BoundingBox(null);
            //房间最小点
            XYZ rbbMin = rbb.get_Bounds(0);
            //房间最大点
            XYZ rbbMax = rbb.get_Bounds(1);

            //立面视图旋转矩阵
            double[][] elevationViewTrans = Matrix.transform2matrix(evBb.Transform);
            //房间最小点转换到立面
            XYZ rbbMinTransformed = new XYZ
            (
                rbbMin.X * elevationViewTrans[0][0] + rbbMin.Y * elevationViewTrans[0][1] + rbbMin.Z * elevationViewTrans[0][2],
                rbbMin.X * elevationViewTrans[1][0] + rbbMin.Y * elevationViewTrans[1][1] + rbbMin.Z * elevationViewTrans[1][2],
                rbbMin.X * elevationViewTrans[2][0] + rbbMin.Y * elevationViewTrans[2][1] + rbbMin.Z * elevationViewTrans[2][2]
            );
            //房间最大点转换到立面
            XYZ rbbMaxTransformed = new XYZ
            (
                rbbMax.X * elevationViewTrans[0][0] + rbbMax.Y * elevationViewTrans[0][1] + rbbMax.Z * elevationViewTrans[0][2],
                rbbMax.X * elevationViewTrans[1][0] + rbbMax.Y * elevationViewTrans[1][1] + rbbMax.Z * elevationViewTrans[1][2],
                rbbMax.X * elevationViewTrans[2][0] + rbbMax.Y * elevationViewTrans[2][1] + rbbMax.Z * elevationViewTrans[2][2]
            );

            #region 重新构造最小点 最大点

            double minX = rbbMinTransformed.X < rbbMaxTransformed.X ? rbbMinTransformed.X : rbbMaxTransformed.X;
            double minY = rbbMinTransformed.Y < rbbMaxTransformed.Y ? rbbMinTransformed.Y : rbbMaxTransformed.Y;

            double maxX = rbbMinTransformed.X > rbbMaxTransformed.X ? rbbMinTransformed.X : rbbMaxTransformed.X;
            double maxY = rbbMinTransformed.Y > rbbMaxTransformed.Y ? rbbMinTransformed.Y : rbbMaxTransformed.Y;

            #endregion 重新构造最小点 最大点

            //最小点偏移后
            XYZ rbbBoundsMinExtended = new XYZ(minX - 1, minY - 1, cbBoundsMin.Z);
            //最大点偏移后
            XYZ rbbBoundsMaxExtended = new XYZ(maxX + 1, maxY + 1, cbBoundsMax.Z);

            evBb.Min = rbbBoundsMinExtended;
            evBb.Max = rbbBoundsMaxExtended;
            elevationView.CropBox = evBb;

            return rbb;
        }

        /// <summary>
        /// 设置立面视图样板
        /// </summary>
        /// <param name="viewTemplate">视图样板</param>
        /// <param name="intElev">立面视图</param>
        public void AssignViewTemplate(View viewTemplate, ViewSection intElev)
        {
            if (viewTemplate == null)
            {
                return;
            }
            intElev.ViewTemplateId = viewTemplate.Id;
        }

        public View GetViewtemplate(Document doc)
        {
            //add pop up to select interior elevation view template
            View viewTemplate = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(v => v as View)
                .Where(v => v.IsTemplate && v.Name.ToLower().Contains("interior") && v.Name.ToLower().Contains("elevation"))
                .FirstOrDefault();
            return viewTemplate;
        }

        /// <summary>
        /// 放置立面
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="center">立面ElevationMarker原点</param>
        /// <param name="intElevPlan">放置立面所在的平面视图</param>
        /// <returns></returns>
        public List<ViewSection> PlaceElevations(Document doc, XYZ center, ViewPlan intElevPlan)
        {
            ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, FindFamilyTypeId(doc), center, 96);
            Parameter p = intElevPlan.get_Parameter(BuiltInParameter.VIEW_PHASE);
            marker.get_Parameter(BuiltInParameter.PHASE_CREATED).Set(p.AsElementId());
            List<ViewSection> intElevList = new List<ViewSection>();

            for (int markerindex = 0; markerindex < marker.MaximumViewCount; markerindex++)
            {
                if (marker.IsAvailableIndex(markerindex))
                {
                    ViewSection intElev = marker.CreateElevation(doc, intElevPlan.Id, markerindex);
                    //设置视图的阶段
                    intElev.get_Parameter(BuiltInParameter.VIEW_PHASE).Set(p.AsElementId());
                    intElevList.Add(intElev);
                }
            }

            return intElevList;
        }

        public ViewPlan GetViewPlanOfRoom(Document doc, List<ViewPlan> intElevPlans, Room r)
        {
            foreach (ViewPlan vp in intElevPlans)
            {
                Room RoominView = new FilteredElementCollector(doc, vp.Id)
                    .OfClass(typeof(SpatialElement))
                    .Select(e => e as Room)
                    .FirstOrDefault(e => e != null && e.Id.IntegerValue == r.Id.IntegerValue);
                if (RoominView != null)
                {
                    return vp;
                }
            }
            return null;
        }

        public List<ViewPlan> DocumentElevPlanViews(Document doc)
        {
            List<ViewPlan> viewIntElevPlans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(x => x.Name.Contains("标高 1"))
                .ToList();

            if (viewIntElevPlans == null)
            {
                throw new Exception("Cannot find View Plans where name containts 'Interior Elevations'");
            }

            return viewIntElevPlans;
        }

        public ElementId FindFamilyTypeId(Document doc)
        {
            ViewFamilyType viewFamilyTypeInterior = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Elevation && x.Name.Contains("立面"));
            if (viewFamilyTypeInterior == null)
            {
                throw new Exception("Cannot find View Family Type containing name Interior ");
            }

            return viewFamilyTypeInterior.Id;
        }
    }
}