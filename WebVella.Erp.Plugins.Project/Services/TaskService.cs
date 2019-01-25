﻿using System;
using System.Collections.Generic;
using System.Linq;
using WebVella.Erp.Api;
using WebVella.Erp.Api.Models;
using WebVella.Erp.Eql;
using WebVella.Erp.Exceptions;
using WebVella.Erp.Web.Models;


//TODO develop service
namespace WebVella.Erp.Plugins.Project.Services
{
	public class TaskService : BaseService
	{
		/// <summary>
		/// Calculates the key and x_search contents and updates the task
		/// </summary>
		/// <param name="data"></param>
		public EntityRecord SetCalculationFields(Guid taskId, out string subject, out Guid projectId)
		{
			subject = "";
			projectId = Guid.Empty;

			EntityRecord patchRecord = new EntityRecord();

			var getTaskResponse = new RecordManager().Find(new EntityQuery("task", "*,$task_type_1n_task.label,$task_status_1n_task.label,$project_nn_task.abbr,$project_nn_task.id", EntityQuery.QueryEQ("id", taskId)));
			if (!getTaskResponse.Success)
				throw new Exception(getTaskResponse.Message);
			if (!getTaskResponse.Object.Data.Any())
				throw new Exception("Task with this Id was not found");

			var taskRecord = getTaskResponse.Object.Data.First();
			subject = (string)taskRecord["subject"];
			var projectAbbr = "";
			var status = "";
			var type = "";
			if (((List<EntityRecord>)taskRecord["$project_nn_task"]).Any())
			{
				var projectRecord = ((List<EntityRecord>)taskRecord["$project_nn_task"]).First();
				if (projectRecord != null && projectRecord.Properties.ContainsKey("abbr"))
				{
					projectAbbr = (string)projectRecord["abbr"];
				}
				if (projectRecord != null) {
					projectId = (Guid)projectRecord["id"];
				}
			}
			if (((List<EntityRecord>)taskRecord["$task_status_1n_task"]).Any())
			{
				var statusRecord = ((List<EntityRecord>)taskRecord["$task_status_1n_task"]).First();
				if (statusRecord != null && statusRecord.Properties.ContainsKey("label"))
				{
					status = (string)statusRecord["label"];
				}
			}
			if (((List<EntityRecord>)taskRecord["$task_type_1n_task"]).Any())
			{
				var typeRecord = ((List<EntityRecord>)taskRecord["$task_type_1n_task"]).First();
				if (typeRecord != null && typeRecord.Properties.ContainsKey("label"))
				{
					type = (string)typeRecord["label"];
				}
			}

			patchRecord["id"] = taskId;
			patchRecord["key"] = projectAbbr + "-" + taskRecord["number"];
			var searchIndex = ""; // fts is included to be used as a default string when you need all records to be returned
			searchIndex += $" {patchRecord["key"]} ";
			searchIndex += $" {taskRecord["subject"]} ";
			searchIndex += $" {taskRecord["body"]} ";
			searchIndex += $" {taskRecord["priority"]} ";


			searchIndex += $" {status} ";
			searchIndex += $" {type} ";


			//Todo Add Comments

			patchRecord["x_search"] = searchIndex;

			return patchRecord;
		}

		public EntityRecordList GetTaskStatuses() {
			var projectRecord = new EntityRecord();
			var eqlCommand = "SELECT * from task_status";
			var eqlParams = new List<EqlParameter>();
			var eqlResult = new EqlCommand(eqlCommand, eqlParams).Execute();
			if (!eqlResult.Any())
				throw new Exception("Error: No task statuses found");

			return eqlResult;
		}

		public EntityRecord GetTask(Guid taskId)
		{
			var projectRecord = new EntityRecord();
			var eqlCommand = " SELECT * from task WHERE id = @taskId";
			var eqlParams = new List<EqlParameter>() { new EqlParameter("taskId", taskId) };

			var eqlResult = new EqlCommand(eqlCommand, eqlParams).Execute();
			if (!eqlResult.Any()) 
				return null;
			else 
				return eqlResult[0];
		}

		public EntityRecordList GetTasks(Guid? projectId, Guid? userId)
		{
			var projectRecord = new EntityRecord();
			var eqlCommand = " SELECT * from task ";
			var eqlParams = new List<EqlParameter>();
			if (projectId != null && userId != null)
			{
				eqlCommand += " WHERE $project_nn_task.id = @projectId AND owner_id = @userId ";
				eqlParams.Add(new EqlParameter("projectId", projectId));
				eqlParams.Add(new EqlParameter("userId", userId));
			}
			else if (projectId != null)
			{
				eqlCommand += " WHERE $project_nn_task.id = @projectId ";
				eqlParams.Add(new EqlParameter("projectId", projectId));
			}
			else if (userId != null) {
				eqlCommand += " WHERE owner_id = @userId ";
				eqlParams.Add(new EqlParameter("userId", userId));
			}

			var eqlResult = new EqlCommand(eqlCommand, eqlParams).Execute();

			return eqlResult;
		}

		public void GetTaskIconAndColor(string priorityValue, out string iconClass, out string color) {
			iconClass = "";
			color = "#fff";

			var priorityOptions = ((SelectField)new EntityManager().ReadEntity("task").Object.Fields.First(x => x.Name == "priority")).Options;
			var recordPriority = priorityOptions.FirstOrDefault(x => x.Value == priorityValue);
			if (recordPriority != null)
			{
				iconClass = recordPriority.IconClass;
				color = recordPriority.Color;
			}

		}

		public void StartTaskTimelog(Guid taskId) {
			var patchRecord = new EntityRecord();
			patchRecord["id"] = taskId;
			patchRecord["timelog_started_on"] = DateTime.Now;
			var updateResponse = new RecordManager().UpdateRecord("task", patchRecord);
			if (!updateResponse.Success)
				throw new Exception(updateResponse.Message);
		}

		public void StopTaskTimelog(Guid taskId)
		{
			//Create transaction
			var patchRecord = new EntityRecord();
			patchRecord["id"] = taskId;
			patchRecord["timelog_started_on"] = null;
			var updateResponse = new RecordManager().UpdateRecord("task", patchRecord);
			if (!updateResponse.Success)
				throw new Exception(updateResponse.Message);
		}

		public EntityRecord GetPageHookLogic(BaseErpPageModel pageModel, EntityRecord record) {

			if (record == null)
				record = new EntityRecord();

			//Preselect owner
			ErpUser currentUser = (ErpUser)pageModel.DataModel.GetProperty("CurrentUser");
			if (currentUser != null)
				record["owner_id"] = currentUser.Id;
			//$project_nn_task.id
			//Preselect project
			if (pageModel.HttpContext.Request.Query.ContainsKey("projectId"))
			{
				var projectQueryId = pageModel.HttpContext.Request.Query["projectId"].ToString();
				if (Guid.TryParse(projectQueryId, out Guid outGuid))
				{
					var projectIdList = new List<Guid>();
					projectIdList.Add(outGuid);
					record["$project_nn_task.id"] = projectIdList;
				}
			}
			else
			{
				var eqlCommand = "SELECT created_on,type_id,$project_nn_task.id FROM task WHERE created_by = @currentUserId ORDER BY created_on PAGE 1 PAGESIZE 1";
				var eqlParams = new List<EqlParameter>() { new EqlParameter("currentUserId", currentUser.Id) };
				var eqlResult = new EqlCommand(eqlCommand, eqlParams).Execute();
				if (eqlResult != null && eqlResult is EntityRecordList && eqlResult.Count > 0)
				{
					var relatedProjects = (List<EntityRecord>)eqlResult[0]["$project_nn_task"];
					if (relatedProjects.Count > 0)
					{
						var projectIdList = new List<Guid>();
						projectIdList.Add((Guid)relatedProjects[0]["id"]);
						record["$project_nn_task.id"] = projectIdList;
					}
					record["type_id"] = (Guid?)eqlResult[0]["type_id"];
				}
			}

			//Preset start date
			record["start_date"] = DateTime.Now;

			return record;
		}


		public void PreCreateRecordPageHookLogic(string entityName, EntityRecord record, List<ErrorModel> errors) {
			var projectIdSubmitted = true;
			if (!record.Properties.ContainsKey("$project_nn_task"))
			{
				projectIdSubmitted = false;
			}
			else
			{
				var projectRecord = (EntityRecord)record["$project_nn_task"];
				if (!projectRecord.Properties.ContainsKey("id"))
				{
					projectIdSubmitted = false;
				}
				else
				{
					if (projectRecord["id"] == null && !(projectRecord["id"] is Guid))
					{
						projectIdSubmitted = false;
					}
				}
			}

			if (!projectIdSubmitted)
			{
				errors.Add(new ErrorModel()
				{
					Key = "$project_nn_task.id",
					Message = "Project is required"
				});
			}
		}

		public void PostCreateApiHookLogic(string entityName, EntityRecord record) {
			//Update key and search fields
			Guid projectId = Guid.Empty;
			string taskSubject = "";
			var patchRecord = new TaskService().SetCalculationFields((Guid)record["id"], subject: out taskSubject, projectId: out projectId);
			var updateResponse = new RecordManager(executeHooks: false).UpdateRecord("task", patchRecord);
			if (!updateResponse.Success)
				throw new Exception(updateResponse.Message);


			//Add activity log
			var subject = $"created <a href=\"/projects/tasks/tasks/r/{patchRecord["id"]}/details\">[{patchRecord["key"]}] {taskSubject}</a>";
			var relatedRecords = new List<string>() { patchRecord["id"].ToString(), projectId.ToString() };
			var scope = new List<string>() { "projects" };
			new FeedItemService().Create(id: Guid.NewGuid(), createdBy: SecurityContext.CurrentUser.Id, subject: subject,
				relatedRecords: relatedRecords, scope: scope, type: "task");
		}

		public void PostUpdateApiHookLogic(string entityName, EntityRecord record)
		{
			//Update key and search fields
			Guid projectId = Guid.Empty;
			string taskSubject = "";
			var patchRecord = new TaskService().SetCalculationFields((Guid)record["id"], subject: out taskSubject, projectId: out projectId);
			var updateResponse = new RecordManager(executeHooks: false).UpdateRecord("task", patchRecord);
			if (!updateResponse.Success)
				throw new Exception(updateResponse.Message);
		}
	}
}
